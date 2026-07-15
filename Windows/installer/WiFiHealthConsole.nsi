Unicode True

!include "MUI2.nsh"

!ifndef APP_ARCH
  !error "APP_ARCH must be provided (x64 or arm64)."
!endif

!ifndef PUBLISH_DIR
  !error "PUBLISH_DIR must point to a dotnet publish directory."
!endif

!ifndef OUTPUT_FILE
  !error "OUTPUT_FILE must be provided."
!endif

!ifndef PRODUCT_VERSION
  !error "PRODUCT_VERSION must be provided from the repository VERSION file."
!endif

!ifndef FILE_VERSION
  !error "FILE_VERSION must be provided from the repository VERSION file."
!endif

!ifndef APP_ICON
  !error "APP_ICON must point to the application .ico file."
!endif

!define APP_NAME "Wi-Fi 体检台"
!define APP_EXE "WiFiHealthConsole.exe"
!define APP_PUBLISHER "meyaomiao"
!define APP_WEBSITE "https://github.com/meyaomiao/wifi-health-console"
!define APP_INSTALL_KEY "Software\WiFiHealthConsole"
!define APP_UNINSTALL_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\WiFiHealthConsole"
!define APP_START_MENU_DIR "Wi-Fi 体检台"

Name "${APP_NAME}"
Caption "${APP_NAME} 安装程序"
BrandingText "${APP_NAME} · Windows ${APP_ARCH}"
OutFile "${OUTPUT_FILE}"
Icon "${APP_ICON}"
UninstallIcon "${APP_ICON}"

InstallDir "$LOCALAPPDATA\Programs\WiFiHealthConsole"
InstallDirRegKey HKCU "${APP_INSTALL_KEY}" "InstallDir"
RequestExecutionLevel user
SetCompressor /SOLID lzma
SetDatablockOptimize on
ShowInstDetails show
ShowUninstDetails show

VIProductVersion "${FILE_VERSION}"
VIAddVersionKey /LANG=2052 "ProductName" "${APP_NAME}"
VIAddVersionKey /LANG=2052 "ProductVersion" "${PRODUCT_VERSION}"
VIAddVersionKey /LANG=2052 "FileVersion" "${PRODUCT_VERSION}"
VIAddVersionKey /LANG=2052 "CompanyName" "${APP_PUBLISHER}"
VIAddVersionKey /LANG=2052 "FileDescription" "${APP_NAME} Windows ${APP_ARCH} 安装程序"
VIAddVersionKey /LANG=2052 "LegalCopyright" "Copyright © ${APP_PUBLISHER}"

!define MUI_ABORTWARNING
!define MUI_ICON "${APP_ICON}"
!define MUI_UNICON "${APP_ICON}"
!define MUI_FINISHPAGE_RUN "$INSTDIR\${APP_EXE}"
!define MUI_FINISHPAGE_RUN_TEXT "启动 ${APP_NAME}"

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_COMPONENTS
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_UNPAGE_FINISH

!insertmacro MUI_LANGUAGE "SimpChinese"
!insertmacro MUI_LANGUAGE "English"

Section "!${APP_NAME}（必需）" SecApplication
  SectionIn RO
  SetShellVarContext current
  SetRegView 64
  SetOverwrite on

  SetOutPath "$INSTDIR"
  File /r /x "*.pdb" "${PUBLISH_DIR}\*.*"

  WriteUninstaller "$INSTDIR\Uninstall.exe"

  CreateDirectory "$SMPROGRAMS\${APP_START_MENU_DIR}"
  CreateShortcut "$SMPROGRAMS\${APP_START_MENU_DIR}\${APP_NAME}.lnk" "$INSTDIR\${APP_EXE}" "" "$INSTDIR\${APP_EXE}" 0
  CreateShortcut "$SMPROGRAMS\${APP_START_MENU_DIR}\卸载 ${APP_NAME}.lnk" "$INSTDIR\Uninstall.exe"

  WriteRegStr HKCU "${APP_INSTALL_KEY}" "InstallDir" "$INSTDIR"
  WriteRegStr HKCU "${APP_INSTALL_KEY}" "Architecture" "${APP_ARCH}"
  WriteRegStr HKCU "${APP_INSTALL_KEY}" "Version" "${PRODUCT_VERSION}"

  WriteRegStr HKCU "${APP_UNINSTALL_KEY}" "DisplayName" "${APP_NAME}"
  WriteRegStr HKCU "${APP_UNINSTALL_KEY}" "DisplayVersion" "${PRODUCT_VERSION}"
  WriteRegStr HKCU "${APP_UNINSTALL_KEY}" "DisplayIcon" "$INSTDIR\${APP_EXE},0"
  WriteRegStr HKCU "${APP_UNINSTALL_KEY}" "Publisher" "${APP_PUBLISHER}"
  WriteRegStr HKCU "${APP_UNINSTALL_KEY}" "URLInfoAbout" "${APP_WEBSITE}"
  WriteRegStr HKCU "${APP_UNINSTALL_KEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "${APP_UNINSTALL_KEY}" "UninstallString" '"$INSTDIR\Uninstall.exe"'
  WriteRegStr HKCU "${APP_UNINSTALL_KEY}" "QuietUninstallString" '"$INSTDIR\Uninstall.exe" /S'
  WriteRegDWORD HKCU "${APP_UNINSTALL_KEY}" "NoModify" 1
  WriteRegDWORD HKCU "${APP_UNINSTALL_KEY}" "NoRepair" 1
SectionEnd

Section /o "桌面快捷方式" SecDesktopShortcut
  SetShellVarContext current
  CreateShortcut "$DESKTOP\${APP_NAME}.lnk" "$INSTDIR\${APP_EXE}" "" "$INSTDIR\${APP_EXE}" 0
SectionEnd

Section "Uninstall"
  SetShellVarContext current
  SetRegView 64

  Delete "$DESKTOP\${APP_NAME}.lnk"
  Delete "$SMPROGRAMS\${APP_START_MENU_DIR}\${APP_NAME}.lnk"
  Delete "$SMPROGRAMS\${APP_START_MENU_DIR}\卸载 ${APP_NAME}.lnk"
  RMDir "$SMPROGRAMS\${APP_START_MENU_DIR}"

  DeleteRegKey HKCU "${APP_UNINSTALL_KEY}"
  DeleteRegKey HKCU "${APP_INSTALL_KEY}"

  ; Only the program directory is removed. Diagnostic history and settings stored
  ; elsewhere under LocalAppData are deliberately retained for reinstalls/upgrades.
  RMDir /r "$INSTDIR"
SectionEnd

LangString DESC_SecApplication ${LANG_SIMPCHINESE} "安装 ${APP_NAME} 主程序与开始菜单快捷方式。"
LangString DESC_SecDesktopShortcut ${LANG_SIMPCHINESE} "在当前用户的桌面创建快捷方式。"
LangString DESC_SecApplication ${LANG_ENGLISH} "Install ${APP_NAME} and its Start menu shortcuts."
LangString DESC_SecDesktopShortcut ${LANG_ENGLISH} "Create a shortcut on the current user's desktop."

!insertmacro MUI_FUNCTION_DESCRIPTION_BEGIN
  !insertmacro MUI_DESCRIPTION_TEXT ${SecApplication} $(DESC_SecApplication)
  !insertmacro MUI_DESCRIPTION_TEXT ${SecDesktopShortcut} $(DESC_SecDesktopShortcut)
!insertmacro MUI_FUNCTION_DESCRIPTION_END
