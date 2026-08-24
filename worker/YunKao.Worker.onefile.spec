# MSIX 专用 PyInstaller one-file 配置，避免把 onedir 的 _internal 目录作为包资源。
from pathlib import Path


project_root = Path(SPECPATH).parent
analysis = Analysis(
    [str(project_root / "worker" / "worker_main.py")],
    pathex=[str(project_root)],
    binaries=[],
    datas=[],
    hiddenimports=["worker.protocol", "docx", "win32com.client", "pythoncom"],
    hookspath=[],
    hooksconfig={},
    runtime_hooks=[],
    excludes=[
        "PySide6",
        "shiboken6",
        "numpy",
        "pandas",
        "scipy",
        "matplotlib",
        "torch",
        "tensorflow",
        "pygame",
        "openpyxl",
        "sqlalchemy",
    ],
    noarchive=False,
)
pyz = PYZ(analysis.pure)
executable = EXE(
    pyz,
    analysis.scripts,
    analysis.binaries,
    analysis.datas,
    [],
    name="YunKao.Worker",
    debug=False,
    bootloader_ignore_signals=False,
    strip=False,
    upx=True,
    console=True,
)
