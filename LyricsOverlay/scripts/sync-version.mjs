/**
 * Copies the single app version (LyricOverlay.csproj) into the studio manifest and lockfile.
 * Runs automatically before every `npm run build`, so the manifest never lags the csproj.
 */

import path from "node:path";
import { readProjectVersion, syncStudioManifest } from "./version.mjs";

const studio = path.resolve(import.meta.dirname, "..");
const csproj = path.join(studio, "..", "LyricOverlay", "LyricOverlay.csproj");

try {
  const { version } = readProjectVersion(csproj);
  const changed = syncStudioManifest(studio, version);
  if (changed.length > 0) {
    console.log(`version ${version}: synced ${changed.join(", ")}`);
  }
} catch (error) {
  console.error(`\n${error.message}`);
  process.exit(1);
}
