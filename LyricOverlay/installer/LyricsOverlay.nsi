; LyricsOverlay — per-user Windows installer.
; Build with:  npm run dist   (from LyricsOverlay/), which passes APP_VERSION from the csproj.
; Manual build: makensis /DAPP_VERSION=1.2.3 /DAPP_VERSION4=1.2.3.0 installer/LyricsOverlay.nsi

Unicode true
SetCompressor /SOLID lzma
SetCompressorDictSize 64
BrandingText "LyricsOverlay ${APP_VERSION}"

; APP_VERSION / APP_VERSION4 are passed in by scripts/dist.mjs, which reads the one version
; declared in LyricOverlay.csproj. Nothing here hardcodes a version number.
!ifndef APP_VERSION
  !error "APP_VERSION is undefined. Build with `npm run dist`, or pass /DAPP_VERSION=1.2.3 to makensis."
!endif
!ifndef APP_VERSION4
  !define APP_VERSION4 "${APP_VERSION}.0"
!endif

!define APP_NAME "LyricsOverlay"
!define APP_ID "com.lyricsoverlay.app"
!define APP_PUBLISHER "LyricsOverlay"
!define APP_EXE "LyricOverlay.exe"
!define REG_UNINSTALL "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_ID}"

; Paths are relative to this script; the publish output is prepared by scripts/dist.mjs.
!define SOURCE_DIR "..\..\dist\app"
!define ICON_FILE "..\Assets\app.ico"

Name "${APP_NAME} ${APP_VERSION}"
OutFile "..\..\dist\LyricsOverlay-Setup-${APP_VERSION}.exe"

; Per-user by default: no administrator prompt, no machine-wide footprint.
RequestExecutionLevel user
InstallDir "$LOCALAPPDATA\Programs\${APP_NAME}"
InstallDirRegKey HKCU "Software\${APP_ID}" "InstallLocation"

VIProductVersion "${APP_VERSION4}"
VIAddVersionKey /LANG=1033 "ProductName" "${APP_NAME}"
VIAddVersionKey /LANG=1033 "FileDescription" "${APP_NAME} setup"
VIAddVersionKey /LANG=1033 "FileVersion" "${APP_VERSION}"
VIAddVersionKey /LANG=1033 "ProductVersion" "${APP_VERSION}"
VIAddVersionKey /LANG=1033 "CompanyName" "${APP_PUBLISHER}"
VIAddVersionKey /LANG=1033 "LegalCopyright" "© ${APP_PUBLISHER}"

!include "MUI2.nsh"
!include "LogicLib.nsh"

!define MUI_ICON "${ICON_FILE}"
!define MUI_UNICON "${ICON_FILE}"
!define MUI_ABORTWARNING
!define MUI_WELCOMEPAGE_TITLE "${APP_NAME} ${APP_VERSION}"
!define MUI_WELCOMEPAGE_TEXT "Synced lyrics drawn over your desktop, controlled from the ${APP_NAME} studio.$\r$\n$\r$\nThis installs for the current Windows user only — no administrator rights needed."
!define MUI_FINISHPAGE_RUN "$INSTDIR\${APP_EXE}"
!define MUI_FINISHPAGE_RUN_TEXT "Start ${APP_NAME}"

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

!insertmacro MUI_LANGUAGE "English"

!include "FileFunc.nsh"

; Close a running copy so files can be replaced; ignore "not running".
!macro StopRunning
  nsExec::Exec 'taskkill /IM ${APP_EXE} /F'
  Pop $0
!macroend

Function .onInit
  ; A previous per-user install keeps its folder.
  ReadRegStr $0 HKCU "Software\${APP_ID}" "InstallLocation"
  ${If} $0 != ""
    StrCpy $INSTDIR $0
  ${EndIf}
FunctionEnd

Section "LyricsOverlay" SecMain
  SectionIn RO
  !insertmacro StopRunning

  SetOutPath "$INSTDIR"
  ; The published app folder: exe, .NET runtime, SkiaSharp/WebView2 natives and Studio assets.
  File /r "${SOURCE_DIR}\*.*"

  WriteUninstaller "$INSTDIR\Uninstall.exe"

  ; Shortcuts: desktop, Start Menu, and a Start Menu entry for the uninstaller.
  CreateShortcut "$DESKTOP\${APP_NAME}.lnk" "$INSTDIR\${APP_EXE}" "" "$INSTDIR\${APP_EXE}" 0
  CreateDirectory "$SMPROGRAMS\${APP_NAME}"
  CreateShortcut "$SMPROGRAMS\${APP_NAME}\${APP_NAME}.lnk" "$INSTDIR\${APP_EXE}" "" "$INSTDIR\${APP_EXE}" 0
  CreateShortcut "$SMPROGRAMS\${APP_NAME}\Uninstall ${APP_NAME}.lnk" "$INSTDIR\Uninstall.exe"

  ; Settings -> Apps entry.
  WriteRegStr HKCU "Software\${APP_ID}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "${REG_UNINSTALL}" "DisplayName" "${APP_NAME}"
  WriteRegStr HKCU "${REG_UNINSTALL}" "DisplayVersion" "${APP_VERSION}"
  WriteRegStr HKCU "${REG_UNINSTALL}" "DisplayIcon" "$INSTDIR\${APP_EXE},0"
  WriteRegStr HKCU "${REG_UNINSTALL}" "Publisher" "${APP_PUBLISHER}"
  WriteRegStr HKCU "${REG_UNINSTALL}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "${REG_UNINSTALL}" "UninstallString" '"$INSTDIR\Uninstall.exe"'
  WriteRegStr HKCU "${REG_UNINSTALL}" "QuietUninstallString" '"$INSTDIR\Uninstall.exe" /S'
  WriteRegDWORD HKCU "${REG_UNINSTALL}" "NoModify" 1
  WriteRegDWORD HKCU "${REG_UNINSTALL}" "NoRepair" 1

  ${GetSize} "$INSTDIR" "/S=0K" $0 $1 $2
  IntFmt $0 "0x%08X" $0
  WriteRegDWORD HKCU "${REG_UNINSTALL}" "EstimatedSize" "$0"

  ; WebView2 ships with Windows 10/11 in practice; warn once instead of failing the install.
  ReadRegStr $1 HKLM "SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}" "pv"
  ReadRegStr $2 HKCU "Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}" "pv"
  ${If} $1 == ""
  ${AndIf} $2 == ""
    IfSilent webview2_checked
    MessageBox MB_OK|MB_ICONINFORMATION "${APP_NAME} needs the Microsoft Edge WebView2 runtime for its studio window.$\r$\n$\r$\nIf the studio does not open, install it from https://developer.microsoft.com/microsoft-edge/webview2/ and start ${APP_NAME} again."
    webview2_checked:
  ${EndIf}

  ; Silent installs (/S) skip the finish page, so relaunch the updated app explicitly.
  ; The in-app updater depends on this to bring LyricsOverlay back after /S.
  ${If} ${Silent}
    Exec '"$INSTDIR\${APP_EXE}"'
  ${EndIf}
SectionEnd

Section "Uninstall"
  !insertmacro StopRunning

  Delete "$DESKTOP\${APP_NAME}.lnk"
  Delete "$SMPROGRAMS\${APP_NAME}\${APP_NAME}.lnk"
  Delete "$SMPROGRAMS\${APP_NAME}\Uninstall ${APP_NAME}.lnk"
  RMDir "$SMPROGRAMS\${APP_NAME}"

  DeleteRegKey HKCU "${REG_UNINSTALL}"
  DeleteRegKey HKCU "Software\${APP_ID}"

  ; Settings and the log in %LOCALAPPDATA%\LyricOverlay are left alone on purpose, so a
  ; reinstall keeps the user's look and timing. Delete that folder to reset the app.
  RMDir /r "$INSTDIR"
SectionEnd
