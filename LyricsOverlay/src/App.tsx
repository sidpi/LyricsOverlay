import { useEffect } from "react";
import { Dashboard } from "./routes/Dashboard";
import { initApp } from "./store/useAppStore";

export default function App() {
  useEffect(() => {
    void initApp();
  }, []);

  return <Dashboard />;
}
