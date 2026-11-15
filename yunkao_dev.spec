# -*- mode: python ; coding: utf-8 -*-

from config.version import (
    APP_ARTIFACT_NAME,
    APP_NAME,
    APP_VERSION,
    APP_VERSION_TUPLE,
)
from PyInstaller.utils.win32.versioninfo import (
    FixedFileInfo,
    StringFileInfo,
    StringStruct,
    StringTable,
    VarFileInfo,
    VarStruct,
    VSVersionInfo,
)


version_info = VSVersionInfo(
    ffi=FixedFileInfo(
        filevers=APP_VERSION_TUPLE,
        prodvers=APP_VERSION_TUPLE,
        mask=0x3F,
        flags=0x0,
        OS=0x40004,
        fileType=0x1,
        subtype=0x0,
        date=(0, 0),
    ),
    kids=[
        StringFileInfo(
            [
                StringTable(
                    "080404B0",
                    [
                        StringStruct("CompanyName", "zhouwu97"),
                        StringStruct("FileDescription", APP_NAME),
                        StringStruct("FileVersion", APP_VERSION),
                        StringStruct("InternalName", APP_ARTIFACT_NAME),
                        StringStruct("LegalCopyright", "© 2026 zhouwu97"),
                        StringStruct(
                            "OriginalFilename",
                            f"{APP_ARTIFACT_NAME}.exe",
                        ),
                        StringStruct("ProductName", APP_NAME),
                        StringStruct("ProductVersion", APP_VERSION),
                    ],
                )
            ]
        ),
        VarFileInfo([VarStruct("Translation", [2052, 1200])]),
    ],
)

hidden_imports = [
    "keyring.backends.Windows",
    "pythoncom",
    "pywintypes",
    "win32com.client",
    "docx",
    "fitz",
    "PIL.Image",
    "PIL.ImageDraw",
    "PIL.ImageFont",
]


a = Analysis(
    ["main.py"],
    pathex=["."],
    binaries=[],
    datas=[],
    hiddenimports=hidden_imports,
    hookspath=[],
    hooksconfig={},
    runtime_hooks=[],
    excludes=[],
    noarchive=False,
    optimize=0,
)
pyz = PYZ(a.pure)

exe = EXE(
    pyz,
    a.scripts,
    [],
    exclude_binaries=True,
    name=APP_ARTIFACT_NAME,
    debug=False,
    bootloader_ignore_signals=False,
    strip=False,
    upx=True,
    console=False,
    disable_windowed_traceback=False,
    argv_emulation=False,
    target_arch=None,
    codesign_identity=None,
    entitlements_file=None,
    icon="app_icon.ico",
    version=version_info,
)

coll = COLLECT(
    exe,
    a.binaries,
    a.datas,
    strip=False,
    upx=True,
    upx_exclude=[],
    name=APP_ARTIFACT_NAME,
)
