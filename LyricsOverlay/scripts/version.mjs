/**
 * The app version lives in exactly one place: <Version> in LyricOverlay.csproj.
 * Everything else — the exe metadata, the installer filename, the studio manifest and its
 * lockfile — is derived from it here.
 */

import { spawnSync } from "node:child_process";
import { existsSync, readFileSync, writeFileSync } from "node:fs";
import path from "node:path";

const isWindows = process.platform === "win32";

/** Reads the version through MSBuild, so csproj imports count too. */
export const readProjectVersion = (csproj) => {
  const result = isWindows
    ? spawnSync(`dotnet msbuild "${csproj}" -getProperty:Version -nologo`, {
        encoding: "utf8",
        shell: true,
      })
    : spawnSync("dotnet", ["msbuild", csproj, "-getProperty:Version", "-nologo"], {
        encoding: "utf8",
      });

  const lines = (result.stdout ?? "").split(/\r?\n/).map((line) => line.trim());
  const version = lines.find((line) => /^\d+\.\d+\.\d+([-.+][0-9A-Za-z.-]+)?$/.test(line)) ?? "";

  if (result.status !== 0 || !version) {
    throw new Error(`could not read a version from ${csproj} (got "${version}")`);
  }

  // NSIS needs four numeric parts for VIProductVersion.
  const numbers = version.split("-")[0].split(".").map((part) => Number.parseInt(part, 10) || 0);
  while (numbers.length < 4) numbers.push(0);

  return { version, version4: numbers.slice(0, 4).join(".") };
};

const writeJson = (file, value) => {
  writeFileSync(file, `${JSON.stringify(value, null, 2)}\n`);
};

/** Writes the version into the studio manifest and lockfile; returns the files it touched. */
export const syncStudioManifest = (studioDir, version) => {
  const changed = [];

  const manifestPath = path.join(studioDir, "package.json");
  const manifest = JSON.parse(readFileSync(manifestPath, "utf8"));
  if (manifest.version !== version) {
    manifest.version = version;
    writeJson(manifestPath, manifest);
    changed.push("package.json");
  }

  const lockPath = path.join(studioDir, "package-lock.json");
  if (existsSync(lockPath)) {
    const lock = JSON.parse(readFileSync(lockPath, "utf8"));
    let touched = false;

    if (lock.version !== version) {
      lock.version = version;
      touched = true;
    }

    const root = lock.packages?.[""];
    if (root && root.version !== version) {
      root.version = version;
      touched = true;
    }

    if (touched) {
      writeJson(lockPath, lock);
      changed.push("package-lock.json");
    }
  }

  return changed;
};
