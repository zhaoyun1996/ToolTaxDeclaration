"""Debug luồng PublicServiceCrawler.GetTaxDecSubmitted.

Điền USERNAME/PASSWORD trước khi chạy. Script tái sử dụng FlowDebugger và luồng
captcha từ test_get_doc_payment_request.py; request/response được ghi tại
debug-get-tax-dec-submitted/. HTML/JSON response thô có thể chứa dữ liệu phiên,
không được commit hoặc chia sẻ.

Yêu cầu: Python 3.9+ và requests.
Chạy: py scripts\test_get_tax_dec_submitted.py
"""

from __future__ import annotations

import base64
import json
import re
import sys
import time
import zipfile
from html.parser import HTMLParser
from pathlib import Path
from typing import Iterator

import test_get_doc_payment_request as common

# ===== CONFIG: điền thông tin test ở đây (không commit thông tin thật) =====
USERNAME = ""
PASSWORD = ""
FROM_DATE = "01/01/2026"
TO_DATE = "31/01/2026"
# Đặt None để xử lý toàn bộ hồ sơ trong khoảng ngày. Giới hạn thấp khi debug.
MAX_RECORDS: int | None = None
# Có thể điền mã hồ sơ lấy trực tiếp từ nút Download trên trình duyệt để
# đối chiếu chính xác với curl thật, không phụ thuộc thứ tự bảng kết quả.
TEST_TRANSACTION_ID = ""
DEBUG_DIR = Path("debug-get-tax-dec-submitted")
REQUEST_DELAY_SECONDS = 1.2


class ResultTableParser(HTMLParser):
    """Đọc nội dung các ô của tất cả dòng trong bảng kết quả DVC."""

    def __init__(self) -> None:
        super().__init__()
        self.rows: list[list[str]] = []
        self._row: list[str] | None = None
        self._cell_parts: list[str] | None = None

    def handle_starttag(self, tag: str, attrs: list[tuple[str, str | None]]) -> None:
        if tag == "tr":
            self._row = []
        elif tag == "td" and self._row is not None:
            self._cell_parts = []

    def handle_data(self, data: str) -> None:
        if self._cell_parts is not None:
            self._cell_parts.append(data)

    def handle_endtag(self, tag: str) -> None:
        if tag == "td" and self._cell_parts is not None and self._row is not None:
            self._row.append(" ".join("".join(self._cell_parts).split()))
            self._cell_parts = None
        elif tag == "tr" and self._row is not None:
            if self._row:
                self.rows.append(self._row)
            self._row = None


def get_total_page(html: str) -> int:
    match = re.search(r'id=["\']totalPage["\'][^>]*>\s*(\d+)', html, re.IGNORECASE)
    return max(1, int(match.group(1))) if match else 1


def get_transaction_ids(html: str) -> list[str]:
    parser = ResultTableParser()
    parser.feed(html)
    # PublicServiceCrawler lấy TransactionID ở cột thứ ba (index 2).
    return [row[2] for row in parser.rows if len(row) >= 12 and row[2].strip()]


def get_detail_csrf(html: str) -> str:
    meta = re.search(r'<meta\s+name=["\']csrf-token["\']\s+content=["\'](.*?)["\']', html, re.IGNORECASE)
    if meta:
        return meta.group(1)
    for pattern in (
        r'name=["\']_csrf["\']\s+value=["\'](.*?)["\']',
        r'id=["\']csrfToken["\']\s+value=["\'](.*?)["\']',
    ):
        match = re.search(pattern, html, re.IGNORECASE)
        if match:
            return match.group(1)
    raise RuntimeError("Không tìm thấy CSRF token ở trang chi tiết hồ sơ")


def get_detail_transaction_id(html: str, fallback: str) -> str:
    match = re.search(r'data-mahoso=["\']([^"\']+)', html, re.IGNORECASE)
    return match.group(1).strip() if match else fallback


def notification_ids(html: str) -> Iterator[str]:
    # Hỗ trợ cả markup data-id mới và callback downloadThongBao cũ.
    seen: set[str] = set()
    for match in re.finditer(r'data-id=["\']([^"\']+)', html, re.IGNORECASE):
        value = match.group(1).strip()
        # downloadThongBao(this, ...) là callback của nút giao diện, không
        # phải mã thông báo; API chỉ nhận ID số.
        if value.isdigit() and value not in seen:
            seen.add(value)
            yield value
    for match in re.finditer(r'downloadThongBao\(["\']?([^,\)"\']+)', html, re.IGNORECASE):
        value = match.group(1).strip()
        if value.isdigit() and value not in seen:
            seen.add(value)
            yield value


def save_download(transaction_id: str, file_name: str, content_base64: str) -> None:
    data = base64.b64decode(content_base64)
    target_dir = DEBUG_DIR / "downloads" / transaction_id
    target_dir.mkdir(parents=True, exist_ok=True)
    target = target_dir / Path(file_name or "declaration.bin").name
    target.write_bytes(data)
    print(f"     đã lưu: {target} ({len(data)} bytes)")

    # Phản chiếu cách crawler giải nén nhưng chặn Zip Slip khi chạy script test.
    if target.suffix.lower() == ".zip":
        extract_dir = target_dir / "unzipped"
        extract_dir.mkdir(exist_ok=True)
        root = extract_dir.resolve()
        with zipfile.ZipFile(target) as archive:
            for entry in archive.infolist():
                destination = (extract_dir / entry.filename).resolve()
                if not str(destination).startswith(str(root)):
                    raise RuntimeError(f"ZIP chứa đường dẫn không hợp lệ: {entry.filename}")
                if not entry.is_dir():
                    destination.parent.mkdir(parents=True, exist_ok=True)
                    destination.write_bytes(archive.read(entry))


def search_page(flow: common.FlowDebugger, captcha: str, page: int):
    params = {
        "loaiThuTuc": "", "maTTHC": "", "maToKhai": "", "maHoSo": "",
        "tuNgay": FROM_DATE, "denNgay": TO_DATE, "scope_tdt1": "SELF",
        "mstUyQuyen_tdt1": "", "captcha": captcha, "page": str(page), "size": "10",
    }
    response = flow.request(
        f"tax_search_page_{page}", flow.dvc, "GET",
        f"{common.PUBLIC_SERVICE_URL}/tthc/ho-so/search", params=params,
        headers={
            "Referer": f"{common.PUBLIC_SERVICE_URL}/tthc/tchs",
            "HX-Request": "true", "HX-Target": "table-container",
            "X-Requested-With": "XMLHttpRequest",
        },
    )
    if "Mã xác nhận" in response.text and any(text in response.text for text in ("không chính xác", "không đúng", "chưa đúng")):
        raise RuntimeError("Captcha tra cứu tờ khai không hợp lệ")
    return response


def download_transaction(flow: common.FlowDebugger, transaction_id: str) -> None:
    time.sleep(REQUEST_DELAY_SECONDS)
    detail = flow.request(
        f"detail_{transaction_id}", flow.dvc, "GET",
        f"{common.PUBLIC_SERVICE_URL}/tthc/tchs/files/detail/{transaction_id}?loai=",
        headers={"Referer": f"{common.PUBLIC_SERVICE_URL}/tthc/tchs"},
    )
    csrf = get_detail_csrf(detail.text)
    transaction_id = get_detail_transaction_id(detail.text, transaction_id)
    print("     cookie names:", ", ".join(sorted(flow.dvc.cookies.keys())))
    declaration_request = {
        "maHoSo": transaction_id,
    }
    declaration_response = flow.request(
        f"download_declaration_{transaction_id}", flow.dvc, "POST",
        f"{common.PUBLIC_SERVICE_URL}/tthc/tchs/downloadhoso",
        check_status=False,
        data=json.dumps(declaration_request, ensure_ascii=False).encode("utf-8"),
        headers={
            "Referer": f"{common.PUBLIC_SERVICE_URL}/tthc/tchs/files/detail/{transaction_id}?loai=",
            "Content-Type": "application/json; charset=utf-8",
            "Accept": "*/*",
            "Origin": common.PUBLIC_SERVICE_URL,
            "X-Requested-With": "XMLHttpRequest",
            "X-XSRF-TOKEN": csrf,
            "Sec-Fetch-Dest": "empty",
            "Sec-Fetch-Mode": "cors",
            "Sec-Fetch-Site": "same-origin",
        },
    )
    if declaration_response.status_code == 429:
        retry_after = declaration_response.headers.get("Retry-After", "5")
        try:
            wait_seconds = min(30, max(3, int(retry_after)))
        except ValueError:
            wait_seconds = 5
        print(f"     DVC rate limit, chờ {wait_seconds}s rồi thử lại...")
        time.sleep(wait_seconds)
        declaration_response = flow.request(
            f"download_declaration_retry_{transaction_id}", flow.dvc, "POST",
            f"{common.PUBLIC_SERVICE_URL}/tthc/tchs/downloadhoso",
            check_status=False,
            data=json.dumps(declaration_request, ensure_ascii=False).encode("utf-8"),
            headers={
                "Referer": f"{common.PUBLIC_SERVICE_URL}/tthc/tchs/files/detail/{transaction_id}?loai=",
                "Content-Type": "application/json; charset=utf-8",
                "Accept": "*/*", "Origin": common.PUBLIC_SERVICE_URL,
                "X-Requested-With": "XMLHttpRequest", "X-XSRF-TOKEN": csrf,
            },
        )
    if declaration_response.status_code >= 400:
        raise RuntimeError(
            f"downloadhoso thất bại: HTTP {declaration_response.status_code}; "
            f"response={declaration_response.text[:500]}"
        )
    declaration = declaration_response.json()
    content = declaration.get("content")
    if not content:
        raise RuntimeError(f"Hồ sơ {transaction_id} không có content tải về: {declaration}")
    save_download(transaction_id, declaration.get("fileName", "declaration.bin"), content)

    for notification_id in notification_ids(detail.text):
        time.sleep(REQUEST_DELAY_SECONDS)
        notification = flow.request(
            f"download_notification_{transaction_id}_{notification_id}", flow.dvc, "POST",
            f"{common.PUBLIC_SERVICE_URL}/tthc/tchs/downloadthongbao",
            json={"idTbao": notification_id, "loaiTBao": ""},
            headers={
                "Referer": f"{common.PUBLIC_SERVICE_URL}/tthc/tchs/files/detail/{transaction_id}?loai=",
                "X-XSRF-TOKEN": csrf,
            },
        ).json()
        if notification.get("content"):
            save_download(
                transaction_id,
                f"notification_{notification_id}_{notification.get('fileName', 'notification.xml')}",
                notification["content"],
            )


def main() -> int:
    # Windows PowerShell có thể dùng code page charmap, không ghi được tiếng Việt.
    # Ép stdout/stderr UTF-8 để lỗi console không che mất lỗi HTTP thật.
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    if hasattr(sys.stderr, "reconfigure"):
        sys.stderr.reconfigure(encoding="utf-8", errors="replace")
    if not USERNAME or not PASSWORD:
        print("Hãy điền USERNAME và PASSWORD ở đầu file trước khi chạy.", file=sys.stderr)
        return 2

    # Dùng chung implementation captcha/logging, nhưng ghi artefact vào thư mục riêng.
    common.USERNAME = USERNAME
    common.PASSWORD = PASSWORD
    common.DEBUG_DIR = DEBUG_DIR
    DEBUG_DIR.mkdir(exist_ok=True)

    try:
        flow = common.FlowDebugger()
        for attempt in range(1, 4):
            try:
                flow.sign_in_dvc()
                break
            except Exception:
                if attempt == 3:
                    raise
                print(f"Đăng nhập/captcha thất bại, thử lại lần {attempt + 1}/3...")
                flow = common.FlowDebugger()
                time.sleep(1)
        flow.request(
            "tax_declaration_page", flow.dvc, "GET", f"{common.PUBLIC_SERVICE_URL}/tthc/tchs",
            headers={"Referer": f"{common.PUBLIC_SERVICE_URL}/tthc/login"},
        )
        captcha_image = flow.request(
            "tax_declaration_captcha", flow.dvc, "GET",
            f"{common.PUBLIC_SERVICE_URL}/tthc/getCaptcha?{int(__import__('time').time() * 1000)}",
            headers={"Referer": f"{common.PUBLIC_SERVICE_URL}/tthc/tchs", "Accept": "image/*,*/*;q=0.8"},
        )
        captcha = flow.solve_captcha(captcha_image.content, f"{common.PUBLIC_SERVICE_URL}/tthc/tchs")

        first_page = search_page(flow, captcha, 1)
        total_page = get_total_page(first_page.text)
        transactions = get_transaction_ids(first_page.text)
        for page in range(2, total_page + 1):
            transactions.extend(get_transaction_ids(search_page(flow, captcha, page).text))

        if TEST_TRANSACTION_ID:
            transactions = [TEST_TRANSACTION_ID]
        if MAX_RECORDS is not None:
            transactions = transactions[:MAX_RECORDS]
        print(f"\nTìm thấy {len(transactions)} hồ sơ cần tải (trong {total_page} trang).")
        failed_transactions: list[dict[str, str]] = []
        for index, transaction_id in enumerate(transactions, start=1):
            print(f"\n[{index}/{len(transactions)}] Xử lý hồ sơ {transaction_id}")
            try:
                download_transaction(flow, transaction_id)
            except Exception as exc:
                failed_transactions.append({"transaction_id": transaction_id, "error": str(exc)})
                print(f"     lỗi hồ sơ, tiếp tục bản ghi kế tiếp: {exc}", file=sys.stderr)
            time.sleep(REQUEST_DELAY_SECONDS)

        (DEBUG_DIR / "failed_transactions.json").write_text(
            json.dumps(failed_transactions, ensure_ascii=False, indent=2), encoding="utf-8"
        )
        if failed_transactions:
            print(f"\nCó {len(failed_transactions)} hồ sơ lỗi; xem failed_transactions.json.")

        print(f"\nHoàn tất. Xem artefact tại: {DEBUG_DIR.resolve()}")
        return 0
    except Exception as exc:
        print(f"\nLuồng dừng vì lỗi: {exc}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
