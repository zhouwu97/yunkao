"""融智云考桌面端统一视觉主题。"""


OVERLAY_STYLE = """
    TampermonkeyFloatingWindow {
        background-color: rgba(255, 255, 255, 222);
        border: 1px solid rgba(210, 224, 239, 232);
        border-radius: 14px;
    }
    QLabel {
        color: #475569;
        font-family: "Microsoft YaHei UI", "Microsoft YaHei", "Segoe UI";
    }
    QLabel#overlayTitle {
        color: #111827;
        font-size: 14px;
        font-weight: 700;
    }
    QLabel#credentialStatus {
        color: #16A34A;
        font-size: 14px;
    }
    QLabel#progressLabel {
        color: #334155;
        background-color: #F8FAFC;
        border: 1px solid #E2E8F0;
        border-radius: 7px;
        padding: 7px 9px;
        font-size: 11px;
    }
    QLabel#statusLabel {
        color: #16A34A;
        font-size: 10px;
        padding: 0 2px;
    }
    QLabel#practiceLabel {
        color: #334155;
        font-size: 11px;
    }
    QPushButton {
        min-height: 32px;
        border: 1px solid #D6DEE8;
        border-radius: 7px;
        padding: 0 11px;
        background-color: #FFFFFF;
        color: #334155;
        font-family: "Microsoft YaHei UI", "Microsoft YaHei", "Segoe UI";
        font-size: 11px;
        font-weight: 600;
    }
    QPushButton:hover {
        border-color: #93C5FD;
        background-color: #F8FAFC;
        color: #1D4ED8;
    }
    QPushButton:pressed { background-color: #EFF6FF; }
    QPushButton#btn_primary {
        background-color: #2563EB;
        border-color: #2563EB;
        color: #FFFFFF;
    }
    QPushButton#btn_primary:hover {
        background-color: #1D4ED8;
        border-color: #1D4ED8;
        color: #FFFFFF;
    }
    QPushButton#btn_primary[extracting="true"] {
        background-color: #DC2626;
        border-color: #DC2626;
    }
    QPushButton#btn_secondary {
        background-color: #FFFFFF;
        color: #475569;
    }
    QPushButton#btn_export {
        background-color: #EFF6FF;
        border-color: #BFDBFE;
        color: #1D4ED8;
    }
    QPushButton#btn_export:hover {
        background-color: #DBEAFE;
        border-color: #93C5FD;
    }
    QPushButton#btn_util {
        min-height: 26px;
        background-color: transparent;
        border-color: transparent;
        color: #64748B;
        font-size: 10px;
        padding: 0 7px;
    }
    QPushButton#btn_util:hover {
        background-color: #F1F5F9;
        border-color: #E2E8F0;
        color: #1D4ED8;
    }
    QPushButton#btn_min {
        min-width: 24px;
        max-width: 24px;
        min-height: 24px;
        max-height: 24px;
        padding: 0;
        background-color: transparent;
        border-color: transparent;
        color: #94A3B8;
        font-size: 15px;
    }
    QPushButton#btn_min:hover {
        background-color: #F1F5F9;
        color: #334155;
    }
    QPushButton:disabled {
        background-color: #F8FAFC;
        border-color: #E2E8F0;
        color: #94A3B8;
    }
    QFrame#practiceRow {
        background-color: rgba(248, 251, 255, 212);
        border: 1px solid rgba(226, 236, 246, 220);
        border-radius: 8px;
    }
    QFrame#overlayDivider {
        min-height: 1px;
        max-height: 1px;
        background-color: #E5E7EB;
        border: none;
    }
"""


SETTINGS_DIALOG_STYLE = """
    QDialog {
        background-color: #FFFFFF;
        color: #111827;
        font-family: "Microsoft YaHei UI", "Microsoft YaHei", "Segoe UI";
        font-size: 13px;
    }
    QFrame#sidebar {
        background-color: #F8FAFC;
        border: none;
        border-right: 1px solid #E5E7EB;
    }
    QLabel#brandTitle {
        color: #111827;
        font-size: 16px;
        font-weight: 700;
    }
    QLabel#brandSubtitle {
        color: #94A3B8;
        font-size: 10px;
    }
    QPushButton#navButton {
        min-height: 48px;
        border: none;
        border-left: 3px solid transparent;
        border-radius: 0;
        padding: 0 22px;
        background-color: transparent;
        color: #374151;
        text-align: left;
        font-size: 14px;
        font-weight: 500;
    }
    QPushButton#navButton:hover {
        background-color: #F1F5F9;
        color: #1D4ED8;
    }
    QPushButton#navButton:checked {
        background-color: #EFF6FF;
        border-left-color: #2563EB;
        color: #1D4ED8;
        font-weight: 700;
    }
    QFrame#contentPanel {
        background-color: #FFFFFF;
        border: none;
    }
    QLabel#pageTitle {
        color: #111827;
        font-size: 24px;
        font-weight: 700;
    }
    QLabel#pageSubtitle {
        color: #6B7280;
        font-size: 12px;
    }
    QFrame#formRow {
        background-color: transparent;
        border: none;
        border-bottom: 1px solid #E5E7EB;
    }
    QLabel#fieldLabel {
        color: #1F2937;
        font-size: 13px;
        font-weight: 500;
    }
    QLabel#fieldDescription {
        color: #6B7280;
        font-size: 10px;
    }
    QLineEdit, QComboBox {
        min-height: 38px;
        background-color: #FFFFFF;
        border: 1px solid #D1D5DB;
        border-radius: 7px;
        padding: 0 11px;
        color: #111827;
        selection-background-color: #BFDBFE;
    }
    QLineEdit:hover, QComboBox:hover { border-color: #9CA3AF; }
    QLineEdit:focus, QComboBox:focus {
        border: 2px solid #2563EB;
        padding: 0 10px;
    }
    QLineEdit:read-only {
        background-color: #F9FAFB;
        color: #4B5563;
    }
    QComboBox::drop-down {
        width: 30px;
        border: none;
        border-left: 1px solid #E5E7EB;
    }
    QComboBox QAbstractItemView {
        background-color: #FFFFFF;
        border: 1px solid #D1D5DB;
        padding: 4px;
        color: #111827;
        selection-background-color: #EFF6FF;
        selection-color: #1D4ED8;
        outline: none;
    }
    QPushButton, QToolButton {
        min-height: 38px;
        border: 1px solid #D1D5DB;
        border-radius: 7px;
        padding: 0 14px;
        background-color: #FFFFFF;
        color: #374151;
        font-weight: 600;
    }
    QPushButton:hover, QToolButton:hover {
        background-color: #F9FAFB;
        border-color: #9CA3AF;
        color: #1D4ED8;
    }
    QPushButton:pressed, QToolButton:pressed { background-color: #F3F4F6; }
    QPushButton#primaryButton {
        min-width: 110px;
        background-color: #2563EB;
        border-color: #2563EB;
        color: #FFFFFF;
    }
    QPushButton#primaryButton:hover {
        background-color: #1D4ED8;
        border-color: #1D4ED8;
        color: #FFFFFF;
    }
    QPushButton#ghostButton {
        min-width: 84px;
        background-color: transparent;
        border-color: transparent;
        color: #4B5563;
    }
    QPushButton#ghostButton:hover {
        background-color: #F3F4F6;
        color: #111827;
    }
    QToolButton#secretButton {
        min-width: 54px;
        max-width: 54px;
        padding: 0 8px;
    }
    QLabel#infoText {
        color: #6B7280;
        background-color: transparent;
        padding: 8px 0;
        font-size: 10px;
    }
    QLabel#warningText {
        color: #B45309;
        background-color: #FFFBEB;
        border: 1px solid #FDE68A;
        border-radius: 7px;
        padding: 8px 10px;
        font-size: 10px;
    }
    QLabel#statusText {
        color: #6B7280;
        font-size: 10px;
    }
    QFrame#contentDivider {
        min-height: 1px;
        max-height: 1px;
        background-color: #E5E7EB;
        border: none;
    }
"""


APP_SHELL_STYLE = """
QMainWindow {
    background-color: #DCE8F7;
    color: #17324A;
    font-family: "Microsoft YaHei UI", "Microsoft YaHei", "Segoe UI";
}
QWidget#appShell {
    background-color: rgba(245, 250, 255, 245);
    border: 1px solid rgba(255, 255, 255, 230);
    border-radius: 20px;
}
QFrame#titleBar {
    background-color: rgba(250, 254, 255, 235);
    border-bottom: 1px solid rgba(188, 210, 236, 150);
}
QLabel#brandMark {
    background-color: #6F9FF5;
    border: 1px solid rgba(255, 255, 255, 220);
    border-radius: 9px;
    color: white;
    font-size: 15px;
    font-weight: 800;
}
QLabel#brandTitle {
    color: #17324A;
    font-size: 15px;
    font-weight: 750;
}
QLabel#brandVersion {
    color: #8495AA;
    font-size: 10px;
}
QLabel#safeStatus {
    padding: 6px 11px;
    border: 1px solid rgba(255, 255, 255, 210);
    border-radius: 11px;
    background-color: rgba(237, 246, 255, 210);
    color: #65B8DD;
    font-size: 10px;
}
QPushButton#windowButton {
    border: none;
    border-radius: 8px;
    background-color: transparent;
    color: #6682A4;
    font-size: 15px;
}
QPushButton#windowButton:hover { background-color: #EDF4FF; color: #315D9C; }
QPushButton#closeWindowButton:hover { background-color: #D86A73; color: white; }
QFrame#navigationRail {
    background-color: rgba(247, 252, 255, 230);
    border: 1px solid rgba(255, 255, 255, 220);
    border-radius: 15px;
}
QPushButton#railButton {
    min-width: 44px;
    border: 1px solid transparent;
    border-radius: 11px;
    background-color: transparent;
    color: #6783A5;
    font-family: "Segoe UI Symbol", "Microsoft YaHei UI";
    font-size: 18px;
}
QPushButton#railButton:hover { background-color: #EEF5FF; color: #5F9FFF; }
QPushButton#railButton:checked {
    border-color: #D8E7FB;
    background-color: #EAF2FF;
    color: #4F88D8;
}
QFrame#browserShell {
    background-color: #FBFDFF;
    border: 1px solid rgba(255, 255, 255, 235);
    border-radius: 15px;
}
QFrame#browserToolbar {
    background-color: #F7FBFF;
    border-bottom: 1px solid #E3ECF7;
    border-top-left-radius: 15px;
    border-top-right-radius: 15px;
}
QPushButton#browserToolButton {
    border: none;
    border-radius: 8px;
    background-color: transparent;
    color: #6A84A6;
    font-size: 18px;
}
QPushButton#browserToolButton:hover { background-color: #EAF2FF; color: #4F88D8; }
QLineEdit#addressBar {
    min-height: 30px;
    border: 1px solid #DCE8F5;
    border-radius: 9px;
    background-color: #ECF4FB;
    color: #7189A6;
    padding: 0 11px;
    font-size: 11px;
}
QWebEngineView#webView { border: none; background-color: #FBFDFF; }
QFrame#controlPanel {
    background-color: rgba(249, 252, 255, 238);
    border: 1px solid rgba(255, 255, 255, 225);
    border-radius: 15px;
}
QLabel#overlayTitle { color: #17324A; font-size: 15px; font-weight: 750; }
QLabel#credentialStatus { color: #65B8DD; font-size: 12px; }
QFrame#statusLine {
    border: 1px solid #E5EEF8;
    border-radius: 10px;
    background-color: #F2F7FD;
}
QLabel#statusDot { color: #65B8DD; font-size: 12px; }
QLabel#statusText { color: #5D7088; font-size: 11px; }
QFrame#progressCard {
    border: 1px solid #E2ECF8;
    border-radius: 13px;
    background-color: #F5F9FF;
}
QLabel#progressBig { color: #315E9B; font-size: 26px; font-weight: 750; }
QLabel#progressTotal { color: #8495AA; font-size: 11px; }
QLabel#progressTag, QLabel#aiTag {
    padding: 4px 7px;
    border-radius: 7px;
    background-color: #F0EDFF;
    color: #7867CA;
    font-size: 10px;
}
QProgressBar#progressBar {
    border: none;
    border-radius: 4px;
    background-color: #E0EAF4;
}
QProgressBar#progressBar::chunk {
    border-radius: 4px;
    background-color: #6C9FF3;
}
QFrame#metric0, QFrame#metric1, QFrame#metric2 {
    border: none;
    border-radius: 9px;
}
QFrame#metric0 { background-color: #EEF5FF; }
QFrame#metric1 { background-color: #F4F0FF; }
QFrame#metric2 { background-color: #FFF5EC; }
QLabel#metricValue { color: #4F88D8; font-size: 13px; font-weight: 700; }
QLabel#metricCaption { color: #8495AA; font-size: 9px; }
QLabel#progressLabel {
    padding: 7px 9px;
    border: 1px solid #E5EEF8;
    border-radius: 8px;
    background-color: #FBFDFF;
    color: #71839A;
    font-size: 10px;
}
QFrame#practiceRow {
    border: 1px solid #E3ECF7;
    border-radius: 9px;
    background-color: #F7FBFF;
}
QLabel#practiceLabel { color: #5D7088; font-size: 10px; }
QLabel#sectionTitle { color: #335475; font-size: 12px; font-weight: 700; }
QLabel#sectionHint { color: #9AA9BC; font-size: 9px; }
QPushButton#btn_primary, QPushButton#btn_stop, QPushButton#btn_secondary,
QPushButton#exportButton, QPushButton#btn_export, QPushButton#btn_util, QPushButton#btn_min, QPushButton#linkButton {
    font-family: "Microsoft YaHei UI", "Microsoft YaHei", "Segoe UI";
}
QPushButton#btn_primary {
    min-height: 36px;
    border: none;
    border-radius: 10px;
    background-color: #6A9DF1;
    color: white;
    font-size: 11px;
    font-weight: 700;
}
QPushButton#btn_primary:hover { background-color: #568DE9; }
QPushButton#btn_primary[extracting="true"] { background-color: #D56F7C; }
QPushButton#btn_stop, QPushButton#btn_secondary {
    min-height: 36px;
    padding: 0 10px;
    border: 1px solid #DCE7F3;
    border-radius: 10px;
    background-color: #FFFFFF;
    color: #637B99;
    font-size: 10px;
}
QPushButton#btn_stop:hover, QPushButton#btn_secondary:hover { border-color: #BBD2EF; color: #4F88D8; }
QPushButton#exportButton, QPushButton#btn_export {
    border: 1px solid #DCE7F3;
    border-radius: 10px;
    background-color: #FFFFFF;
    color: #55739A;
    font-size: 10px;
}
QPushButton#exportButton:hover, QPushButton#btn_export:hover { border-color: #B8D0EE; background-color: #F5F9FF; color: #4F88D8; }
QPushButton#exportButton:disabled, QPushButton#btn_export:disabled { background-color: #F5F8FC; color: #AAB8C8; }
QFrame#aiCard {
    border: 1px solid #E3DDF8;
    border-radius: 10px;
    background-color: #F4F1FF;
}
QLabel#aiTitle { color: #5D54A5; font-size: 10px; font-weight: 700; }
QLabel#aiSubtitle { color: #958DBA; font-size: 9px; }
QListWidget#eventList {
    border: 1px solid #E5EEF8;
    border-radius: 9px;
    background-color: #FBFDFF;
    color: #617B9E;
    font-size: 10px;
    padding: 4px;
}
QListWidget#eventList::item { padding: 5px 4px; border-bottom: 1px solid #F0F4F9; }
QLabel#statusLabel { color: #65B8DD; font-size: 10px; }
QLabel#panelFooter { color: #9AA9BC; font-size: 9px; }
QPushButton#btn_util, QPushButton#btn_min, QPushButton#linkButton {
    border: none;
    border-radius: 8px;
    background-color: transparent;
    color: #6A84A6;
    font-size: 10px;
}
QPushButton#btn_util:hover, QPushButton#btn_min:hover, QPushButton#linkButton:hover { background-color: #EDF4FF; color: #4F88D8; }
QScrollArea#panelScroll { border: none; background: transparent; }
"""
