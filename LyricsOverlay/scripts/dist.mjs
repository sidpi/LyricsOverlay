/**
 * Production build: studio -> published desktop app -> NSIS installer.
 *
 *   npm run dist
 *
 * Output: <repo>/dist/LyricsOverlay-Setup-<version>.exe
 *
 * The version comes from <Version> in LyricOverlay.csproj - the one place it is declared - and
 * is passed to NSIS as APP_VERSION, so the installer filename, its version metadata and the
 * exe's own file version can never disagree.
 *
 * makensis is taken from NSIS_MAKENSIS (env), then PATH, then the bundled
 * ../LyricOverlay/tools/nsis/Bin/makensis.exe (NSIS 3.11 portable).
 */

import { spawnSync } from "node:child_process";
import { existsSync, rmSync, mkdirSync, statSync, readdirSync } from "node:fs";
import path from "node:path";
import { readProjectVersion, syncStudioManifest } from "./version.mjs";

const studio = path.resolve(import.meta.dirname, "..");
const app = path.resolve(studio, "..");
const project = path.join(app, "LyricOverlay");
const csproj = path.join(project, "LyricOverlay.csproj");
const dist = path.join(app, "dist");
const publishDir = path.join(dist, "app");
const isWindows = process.platform === "win32";

const quote = (value) => (value.includes(" ") ? `"${value}"` : value);

const spawn = (command, args, cwd, stdio = "inherit") => {
  // Windows needs a shell to run npm.cmd; hand it a single quoted command line.
  if (isWindows) {
    return spawnSync([command, ...args].map(quote).join(" "), { cwd, stdio, shell: true });
  }

  return spawnSync(command, args, { cwd, stdio });
};

const run = (command, args, cwd) => {
  console.log(`\n$ ${command} ${args.join(" ")}`);
  const result = spawn(command, args, cwd);
  if (result.status !== 0) {
    console.error(`\nfailed: ${command} exited with ${result.status ?? result.signal}`);
    process.exit(result.status ?? 1);
  }
};

const findMakensis = () => {
  if (process.env.NSIS_MAKENSIS && existsSync(process.env.NSIS_MAKENSIS)) {
    return process.env.NSIS_MAKENSIS;
  }

  const bundled = path.join(project, "tools", "nsis", "Bin", "makensis.exe");
  if (existsSync(bundled)) {
    return bundled;
  }

  const probe = spawn("makensis", ["/VERSION"], project, "ignore");
  if (probe.status === 0) {
    return "makensis";
  }

  console.error(
    "\nmakensis not found. Install NSIS 3 (https://nsis.sourceforge.io/Download), put it on PATH,\n" +
      `or unzip the portable build into ${path.join(project, "tools", "nsis")}.\n` +
      "You can also set NSIS_MAKENSIS to the full path of makensis.exe.",
  );
  process.exit(1);
};

/** Total bytes of a directory tree, for honest payload reporting. */
const directorySize = (dir) =>
  readdirSync(dir, { withFileTypes: true }).reduce((total, entry) => {
    const full = path.join(dir, entry.name);
    return total + (entry.isDirectory() ? directorySize(full) : statSync(full).size);
  }, 0);

/**
 * Drops build leftovers that are never read at runtime. Native SkiaSharp alone used to ship
 * ~120 MB of .pdb this way, which is most of the old install size.
 */
const stripBuildArtifacts = (dir) => {
  let removed = 0;
  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) {
      removed += stripBuildArtifacts(full);
    } else if (/\.(pdb|xml|map)$/i.test(entry.name)) {
      rmSync(full, { force: true });
      removed += 1;
    }
  }
  return removed;
};

let version;
let version4;
try {
  ({ version, version4 } = readProjectVersion(csproj));
} catch (error) {
  console.error(`\n${error.message}`);
  process.exit(1);
}

// The studio manifest is generated from the same value, so it can never drift.
for (const file of syncStudioManifest(studio, version)) {
  console.log(`synced ${file} to ${version}`);
}

const installer = path.join(dist, `LyricsOverlay-Setup-${version}.exe`);
console.log(`\nversion ${version} (from ${path.relative(app, csproj)}), installer: ${installer}`);

// 1. Studio bundle (Vite writes ../LyricOverlay/Studio).
run(isWindows ? "npm.cmd" : "npm", ["run", "build"], studio);

// 2. Self-contained desktop app: no .NET install required on the target PC.
rmSync(publishDir, { recursive: true, force: true });
mkdirSync(publishDir, { recursive: true });
run(
  "dotnet",
  [
    "publish",
    path.join(project, "LyricOverlay.csproj"),
    "-c",
    "Release",
    "-r",
    "win-x64",
    "--self-contained",
    "true",
    "-p:PublishSingleFile=true",
    "-p:EnableCompressionInSingleFile=true",
    "-p:IncludeNativeLibrariesForSelfExtract=true",
    "-p:PublishReadyToRun=false",
    "-p:DebugType=none",
    "-p:DebugSymbols=false",
    "-p:GenerateDocumentationFile=false",
    "-o",
    publishDir,
  ],
  project,
);

const removed = stripBuildArtifacts(publishDir);
if (removed > 0) {
  console.log(`removed ${removed} non-runtime file(s) (*.pdb, *.xml, *.map) from the payload`);
}

const exe = path.join(publishDir, "LyricOverlay.exe");
if (!existsSync(exe)) {
  console.error(`\npublish did not produce ${exe}`);
  process.exit(1);
}

// WebView2Loader.dll is inside the single file now; only the on-disk studio assets are checked.
for (const asset of ["Studio/index.html", "Studio/logo.png"]) {
  if (!existsSync(path.join(publishDir, asset))) {
    console.error(`\npublished app is missing ${asset}`);
    process.exit(1);
  }
}

// 3. NSIS setup.
const makensis = findMakensis();
rmSync(installer, { force: true });
run(
  makensis,
  [
    "/V2",
    `/DAPP_VERSION=${version}`,
    `/DAPP_VERSION4=${version4}`,
    path.join(project, "installer", "LyricsOverlay.nsi"),
  ],
  project,
);

if (!existsSync(installer)) {
  console.error(`\ninstaller was not created: ${installer}`);
  process.exit(1);
}

const installerMb = (statSync(installer).size / (1024 * 1024)).toFixed(1);
const payloadMb = (directorySize(publishDir) / (1024 * 1024)).toFixed(1);
console.log(`\ninstaller: ${installer}`);
console.log(`size: ${installerMb} MB  (app payload: ${payloadMb} MB in ${publishDir})`);
