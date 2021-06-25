import "./i18n/i18n";
import { ThemeProvider } from "@fluentui/react";
import React from "react";
import { Route } from "react-router";
import { Callers } from "./features/Callers/Callers";
import { EventList } from "./features/EventList/EventList";
import { Layout } from "./components/Layout";
import { ModuleList } from "./features/ModuleList/ModuleList";
import { ProcessChooser } from "./features/ProcessChooser/ProcessChooser";
import { ProcessList } from "./features/ProcessChooser/ProcessList/ProcessList";
import { SourceViewer } from "./components/SourceViewer";
import { DataFileContextProvider } from "./context/DataFileContext";
import { EventViewer } from "./features/EventViewer";
import { Hotspots } from "features/Hotspots/Hotspots";
import { Toaster } from "react-hot-toast";
import { Routes } from "common/Routes";
import { RouteKeyContextProvider } from "context/RouteContext";
import { Home } from "features/Home/Home";
import { TraceInfo } from "features/StackViewerFilter/TraceInfo/TraceInfo";
import { ProcessInfo } from "features/ProcessChooser";
import { AvailableThemes, Header, IThemeMap } from "features/Header/Header";
import { useLocalStorage } from "hooks/useLocalStorage";

const App: React.FC = () => {
  const [themeKey] = useLocalStorage<keyof IThemeMap>("theme", "Light");
  return (
    <ThemeProvider applyTo="body" theme={AvailableThemes[themeKey]}>
      <DataFileContextProvider>
        <>
          <Header />
          <RouteKeyContextProvider>
            <Layout>
              <Route exact path={Routes.IndexHtml} component={Home} />
              <Route exact path={Routes.Root} component={Home} />
              <Route exact path={Routes.UI} component={Home} />
              <Route path={Routes.ProcessInfo} component={ProcessInfo} />
              <Route path={Routes.TraceInfo} component={TraceInfo} />
              <Route path={Routes.ProcessList} component={ProcessList} />
              <Route path={Routes.ModuleList} component={ModuleList} />
              <Route path={Routes.EventViewer} component={EventViewer} />
              <Route path={Routes.EventList} component={EventList} />
              <Route path={Routes.ProcessChooser} component={ProcessChooser} />
              <Route path={Routes.HotSpots} component={Hotspots} />
              <Route path={Routes.Callers} component={Callers} />
              <Route path={Routes.SourceViewer} component={SourceViewer} />
            </Layout>
          </RouteKeyContextProvider>
        </>
      </DataFileContextProvider>
      <Toaster
        position="top-right"
        containerStyle={{
          zIndex: 1000001,
        }}
        toastOptions={{
          error: {
            duration: Infinity,
            style: {
              backgroundColor: AvailableThemes[themeKey].semanticColors.severeWarningBackground,
              color: AvailableThemes[themeKey].semanticColors.bodyText,
            },
          },
          success: {
            duration: 5000,
            style: {
              backgroundColor: AvailableThemes[themeKey].semanticColors.successBackground,
              color: AvailableThemes[themeKey].semanticColors.bodyText,
            },
          },
        }}
      />
    </ThemeProvider>
  );
};

export { App };
