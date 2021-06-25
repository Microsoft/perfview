const isDev = require("electron-is-dev");

const { app, ipcMain } = require("electron");
const { BrowserWindow } = require("electron");
const { protocol } = require("electron");
const path = require("path");
const fs = require("fs");
const os = require("os");
const cProcess = require("child_process").spawn;
const portscanner = require("portscanner");
const { askForFoldersAccess } = require("node-mac-permissions");
const { initSplashScreen } = require("@trodi/electron-splashscreen");
let io, server, apiProcess;
let launchFile;
let launchUrl;
let win;
let binaryFile = "PerfViewJS";
const mainOpts = {
  width: 1024,
  height: 768,
  minWidth: 720,
  minHeight: 576,
  webPreferences: {
    nodeIntegration: false,
    nodeIntegrationInWorker: false,
    nodeIntegrationInSubFrames: false,
    contextIsolation: true,
    preload: path.join(app.getAppPath(), "public/preload.js"),
  },
};
const config = {
  windowOpts: { ...mainOpts, resizable: false },
  templateUrl: `${__dirname}/splashscreen/index.html`,
  splashScreenOpts: {
    width: 650,
    height: 382,
    transparent: true,
  },
};

let tmpDir = path.join(
  app.getPath("home") + "/" + "Downloads" + "/" + "PerfViewJS"
);

function createWindow() {
  // Create the browser window.
  win = initSplashScreen(config);

  // and load the index.html of the app.
  // win.loadFile("index.html");
  win.loadURL(
    isDev
      ? "http://localhost:3000"
      : `file://${path.join(__dirname, "../build/index.html")}`
  );
  // Open the DevTools.
  if (isDev) {
    win.webContents.openDevTools({ mode: "detach" });
  }
}

// This method will be called when Electron has finished
// initialization and is ready to create browser windows.
// Some APIs can only be used after this event occurs.
app.whenReady().then(createWindow);

// Quit when all windows are closed, except on macOS. There, it's common
// for applications and their menu bar to stay active until the user quits
// explicitly with Cmd + Q.
app.on("window-all-closed", () => {
  if (process.platform !== "darwin") {
    app.quit();
  }
});

app.on("activate", () => {
  if (BrowserWindow.getAllWindows().length === 0) {
    createWindow();
  }
});

ipcMain.on("toMain", (event, filePath) => {
  const source = filePath;
  const destination = path.join(tmpDir + "/" + path.basename(filePath));
  fs.copyFile(source, destination, (err) => {
    if (err) throw err;
  });
  win.webContents.send("fromMain", "reload");
});

let currentBinPath = path.join(__dirname.replace("app.asar", ""), "bin");

//  handle macOS events for opening the app with a file, etc
app.on("will-finish-launching", () => {
  app.on("open-file", (evt, file) => {
    evt.preventDefault();
    launchFile = file;
  });
  app.on("open-url", (evt, url) => {
    evt.preventDefault();
    launchUrl = url;
  });
});

app.on("ready", () => {
  askForFoldersAccess("downloads").then((status) => {
    console.log(`Access to downloads is ${status}`);
  });
  //create tmp dir if not exists on
  if (!fs.existsSync(tmpDir)) {
    fs.mkdirSync(tmpDir);
  }

  // Fix ERR_UNKNOWN_URL_SCHEME using file protocol
  // https://github.com/electron/electron/issues/23757
  protocol.registerFileProtocol("file", (request, callback) => {
    const pathname = request.url.replace("file:///", "");
    callback(pathname);
  });
  startServer();
});

app.on("quit", async () => {
  await server.close();

  // try graceful shutdown (SIGTERM)
  if (!apiProcess.kill()) {
    // force shutdown (SIGKILL)
    apiProcess.kill(9);
  }
});

app.on("before-quit", async (event) => {
  if (fs.existsSync(tmpDir)) {
    event.preventDefault();
    try {
      await fs.rmdir(tmpDir, { recursive: true }, (err) => {
        if (err) console.log(err);
        console.log("cleared tmp dir");
        app.quit();
      });
    } catch (err) {
      console.error(err);
    }
  }
});

function startServer() {
  // Added default port as configurable for port restricted environments.
  let defaultElectronPort = 4999;

  //hostname needs to be localhost, otherwise Windows Firewall will be triggered.
  portscanner.findAPortNotInUse(
    defaultElectronPort,
    65535,
    "localhost",
    function (error, port) {
      console.log("Electron Socket IO Port: " + defaultElectronPort);
      startSocketApiBridge(defaultElectronPort);
    }
  );
}

function startSocketApiBridge(port) {
  // instead of 'require('socket.io')(port);' we need to use this workaround
  // otherwise the Windows Firewall will be triggered
  server = require("http").createServer();
  io = require("socket.io")();
  io.attach(server);

  server.listen(port, "localhost");
  server.on("listening", function () {
    console.log(
      "Electron Socket started on port %s at %s",
      server.address().port,
      server.address().address
    );
    // Now that socket connection is established, we can guarantee port will not be open for portscanner
    startAspCoreBackend(port);
  });

  // prototype
  app["mainWindowURL"] = "";
  app["mainWindow"] = null;

  io.on("connection", (socket) => {
    socket.on("disconnect", function (reason) {
      console.log("Got disconnect! Reason: " + reason);
    });

    if (global["electronsocket"] === undefined) {
      global["electronsocket"] = socket;
      global["electronsocket"].setMaxListeners(0);
    }

    console.log(
      "ASP.NET Core Application connected...",
      "global.electronsocket",
      global["electronsocket"].id,
      new Date()
    );

    socket.on("register-app-open-file-event", (id) => {
      electronSocket = socket;

      app.on("open-file", (event, file) => {
        event.preventDefault();

        electronSocket.emit("app-open-file" + id, file);
      });

      if (launchFile) {
        electronSocket.emit("app-open-file" + id, launchFile);
      }
    });

    socket.on("register-app-open-url-event", (id) => {
      electronSocket = socket;
      app.on("open-url", (event, url) => {
        event.preventDefault();
        electronSocket.emit("app-open-url" + id, url);
      });

      if (launchUrl) {
        electronSocket.emit("app-open-url" + id, launchUrl);
      }
    });
  });
}

function startAspCoreBackend(electronPort) {
  // hostname needs to be localhost, otherwise Windows Firewall will be triggered.
  portscanner.findAPortNotInUse(
    electronPort + 1,
    65535,
    "localhost",
    function (error, electronWebPort) {
      startBackend(electronWebPort);
    }
  );

  function startBackend(aspCoreBackendPort) {
    if (os.platform() === "win32") {
      binaryFile = binaryFile + ".exe";
    }
    console.log("ASP.NET Core Port: " + aspCoreBackendPort);
    console.log("ASP.NET Core tmp dir: " + tmpDir);

    const parameters = [aspCoreBackendPort, tmpDir];

    let binFilePath = path.join(currentBinPath, binaryFile);
    var options = { cwd: currentBinPath };
    console.log("starting backend ASP.NET Core Port: " + aspCoreBackendPort);
    apiProcess = cProcess(binFilePath, parameters, options);

    apiProcess.stdout.on("data", (data) => {
      console.log(`stdout: ${data.toString()}`);
    });
  }
}
