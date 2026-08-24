# PyInstaller onedir 配置：不把 UI、凭据或配置文件带入 Worker。
from pathlib import Path

project_root = Path(SPECPATH).parent
hiddenimports = [
    "worker.protocol",
]
for optional_module in ("docx", "win32com.client", "pythoncom"):
    hiddenimports.append(optional_module)

analysis = Analysis(
    [str(project_root / "worker" / "worker_main.py")],
    pathex=[str(project_root)],
    binaries=[],
    datas=[],
    hiddenimports=hiddenimports,
    hookspath=[],
    hooksconfig={},
    runtime_hooks=[],
    # Worker 只需要 bs4、docx、图片处理和 WPS/Word COM；排除开发机上的
    # PySide6、科学计算和训练框架，避免 PyInstaller 把无关依赖带入 MSIX。
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
    [],
    exclude_binaries=True,
    name="YunKao.Worker",
    debug=False,
    bootloader_ignore_signals=False,
    strip=False,
    upx=True,
    console=True,
)
collated = COLLECT(
    executable,
    analysis.binaries,
    analysis.datas,
    strip=False,
    upx=True,
    name="YunKao.Worker",
)
