import { initializeIcons } from "@fluentui/react/lib/Icons";
import React from "react";
import ReactDOM from "react-dom";
import { HashRouter as Router } from "react-router-dom";
import "./index.css";
import { App } from "./App";

const baseUrl = document.querySelectorAll("base")[0].getAttribute("href");
const rootElement = document.querySelector("#root");

initializeIcons();

ReactDOM.render(
  <Router basename={baseUrl === null ? "BrokenUrl" : baseUrl}>
    <App />
  </Router>,
  rootElement
);
