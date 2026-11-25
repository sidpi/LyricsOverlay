import React from "react";
import ReactDOM from "react-dom/client";

import "@fontsource/playfair-display/400.css";

import "./styles.css";
import App from "./App";

ReactDOM.createRoot(document.getElementById("root") as HTMLElement).render(
  <React.StrictMode>
    <App />
  </React.StrictMode>,
);
