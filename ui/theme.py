"""融智云考桌面端统一视觉主题。"""


OVERLAY_STYLE = """
    TampermonkeyFloatingWindow {
        background-color: rgba(255, 255, 255, 250);
        border: 1px solid #D6DEE8;
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
        background-color: #F8FAFC;
        border: 1px solid #E2E8F0;
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
