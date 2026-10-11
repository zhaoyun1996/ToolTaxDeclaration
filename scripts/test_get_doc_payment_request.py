"""Chạy debug độc lập luồng PublicServiceCrawler.GetDocPaymentRequest.

Điền USERNAME và PASSWORD ở khối CONFIG trước khi chạy. Script ghi request/response
(đã che token, cookie, captcha và mật khẩu) vào debug-get-doc-payment-request/.

Yêu cầu: Python 3.9+ và requests (`py -m pip install requests`).
"""

from __future__ import annotations

import base64
import json
import re
import sys
import time
from datetime import date
from html.parser import HTMLParser
from pathlib import Path
from typing import Any, Mapping
from urllib.parse import parse_qs, unquote, urljoin, urlparse

import requests

# ===== CONFIG: điền thông tin test ở đây (không commit thông tin thật) =====
USERNAME = "0318585992-ql"
PASSWORD = "12345678@Abc"
FROM_DATE = "01/01/2026"  # định dạng eTax đang dùng
TO_DATE = date.today().strftime("%d/%m/%Y")

PUBLIC_SERVICE_URL = "https://dichvucong.gdt.gov.vn"
ETAX_URL = "https://thuedientu.gdt.gov.vn"
# Tương đương MisaCaptchaClient.Decaptcha trong ứng dụng .NET.
CAPTCHA_API_URL = "https://aspapp.misa.vn/Api/api/ClientToolAction/CaptchaDecode"
TIMEOUT_SECONDS = 45
DEBUG_DIR = Path("debug-get-doc-payment-request")

SENSITIVE_NAMES = {
    "password", "matkhau", "captcha", "_csrf", "x-xsrf-token",
    "dse_sessionid", "cookie", "authorization",
}


class FormInputParser(HTMLParser):
    """Lấy input của form `mainForm`, giống ExtractFormInputs trong C#."""

    def __init__(self) -> None:
        super().__init__()
        self.in_target_form = False
        self.seen_form = False
        self.inputs: dict[str, str] = {}

    def handle_starttag(self, tag: str, attrs: list[tuple[str, str | None]]) -> None:
        attrs_map = dict(attrs)
        if tag == "form":
            form_id = attrs_map.get("id")
            if form_id == "mainForm" or not self.seen_form:
                self.in_target_form = True
                self.seen_form = True
        if tag == "input" and self.in_target_form:
            name = attrs_map.get("name")
            if name:
                self.inputs[name] = attrs_map.get("value") or ""

    def handle_endtag(self, tag: str) -> None:
        if tag == "form" and self.in_target_form:
            self.in_target_form = False


def redact(value: Any, name: str = "") -> Any:
    if name.lower() in SENSITIVE_NAMES:
        return "***REDACTED***"
    if isinstance(value, bytes):
        return value.decode("utf-8", errors="replace")
    if isinstance(value, Mapping):
        return {key: redact(val, key) for key, val in value.items()}
    if isinstance(value, (list, tuple)):
        return [redact(item, name) for item in value]
    return value


class FlowDebugger:
    def __init__(self) -> None:
        self.dvc = requests.Session()
        self.etax = requests.Session()
        self.csrf_token = ""
        self.etax_session_id = ""
        self.processor_id = ""
        self.query_page_id = ""
        self.sequence = 0
        self.dvc.headers.update({
            "Accept-Language": "vi-VN,vi;q=0.9,en-US;q=0.8,en;q=0.7",
            "User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/120 Safari/537.36",
        })
        self.etax.headers.update({
            "Accept-Language": "en-US,en;q=0.9",
            "User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/154 Safari/537.36",
        })

    def request(
        self, label: str, session: requests.Session, method: str, url: str,
        check_status: bool = True, **kwargs: Any
    ) -> requests.Response:
        self.sequence += 1
        request_info = {
            "label": label,
            "method": method.upper(),
            "url": self.redact_url(url),
            "headers": redact(kwargs.get("headers", {})),
            "params": redact(kwargs.get("params", {})),
            "data": redact(kwargs.get("data", {})),
            "json": redact(kwargs.get("json", {})),
        }
        print(f"\n[{self.sequence:02}] {label}: {method.upper()} {url}")
        print(json.dumps(request_info, ensure_ascii=False, indent=2))
        response = session.request(
            method, url, timeout=TIMEOUT_SECONDS, allow_redirects=False, **kwargs
        )
        response_info = {
            "status_code": response.status_code,
            "url": self.redact_url(response.url),
            "headers": redact(dict(response.headers)),
            "body_preview": redact(response.text[:2000]),
        }
        print(f"     => HTTP {response.status_code}, {len(response.content)} bytes")
        (DEBUG_DIR / f"{self.sequence:02}_{label}_request.json").write_text(
            json.dumps(request_info, ensure_ascii=False, indent=2), encoding="utf-8"
        )
        (DEBUG_DIR / f"{self.sequence:02}_{label}_response.json").write_text(
            json.dumps(response_info, ensure_ascii=False, indent=2), encoding="utf-8"
        )
        (DEBUG_DIR / f"{self.sequence:02}_{label}_response.html").write_bytes(response.content)
        if check_status:
            response.raise_for_status()
        return response

    @staticmethod
    def redact_url(url: str) -> str:
        """Che token trong query string, vẫn giữ đường dẫn để đối chiếu request."""
        parsed = urlparse(url)
        query = parse_qs(parsed.query, keep_blank_values=True)
        values = []
        for key, items in query.items():
            for item in items:
                values.append(f"{key}={'***REDACTED***' if key.lower() in SENSITIVE_NAMES else item}")
        return parsed._replace(query="&".join(values)).geturl()

    @staticmethod
    def extract_csrf(html: str) -> str:
        match = re.search(r'name="_csrf"\s+value="(.*?)"', html)
        if not match:
            match = re.search(r'value="(.*?)"\s+name="_csrf"', html)
        if not match:
            raise RuntimeError("Không tìm thấy _csrf trên trang login DVC")
        return match.group(1)

    @staticmethod
    def form_inputs(html: str) -> dict[str, str]:
        parser = FormInputParser()
        parser.feed(html)
        return parser.inputs

    @staticmethod
    def normalize_etax_url(url: str) -> str:
        url = url.strip()
        if url.startswith(("http://", "https://")):
            return url
        if not url.startswith("/"):
            url = "/" + url
        if not url.lower().startswith("/etaxnnt/") and url.lower() != "/etaxnnt":
            url = "/etaxnnt" + url
        return urljoin(ETAX_URL, url)

    @staticmethod
    def redirect_from(response: requests.Response) -> str:
        location = response.headers.get("Location", "")
        if not location:
            match = re.search(r"window\.location\.href\s*=\s*'([^']+)'", response.text)
            if match:
                location = match.group(1)
        if not location:
            raise RuntimeError("Không tìm được Location hoặc window.location.href trong response")
        return location

    def solve_captcha(self, image: bytes, referer: str) -> str:
        response = self.request(
            "captcha_decode", requests.Session(), "POST", CAPTCHA_API_URL,
            params={"domainName": referer, "source": "dichvucong"},
            files={"image": ("captcha.jpg", image, "image/jpeg")},
        )
        payload = response.json()
        if payload.get("Code") != 200 or not payload.get("Data"):
            raise RuntimeError(f"Captcha API không giải được: {payload}")
        return str(payload["Data"]).strip()

    def sign_in_dvc(self) -> None:
        self.request("01_home_login", self.dvc, "GET", f"{PUBLIC_SERVICE_URL}/tthc/homelogin")
        login = self.request("02_login_page", self.dvc, "GET", f"{PUBLIC_SERVICE_URL}/tthc/login")
        self.csrf_token = self.extract_csrf(login.text)
        captcha_response = self.request(
            "03_login_captcha", self.dvc, "GET",
            f"{PUBLIC_SERVICE_URL}/tthc/login/getCaptcha?{int(time.time() * 1000)}",
            headers={"Referer": f"{PUBLIC_SERVICE_URL}/tthc/login", "Accept": "image/*,*/*;q=0.8"},
        )
        captcha = self.solve_captcha(captcha_response.content, f"{PUBLIC_SERVICE_URL}/tthc/login")
        login_response = self.request(
            "04_login_ldap", self.dvc, "POST", f"{PUBLIC_SERVICE_URL}/tthc/loginLDAP",
            data={
                "tenDN": USERNAME,
                "matKhau": base64.b64encode(PASSWORD.encode("utf-8")).decode("ascii"),
                "doiTuong": "DN",
                "captcha": captcha,
            },
            headers={
                "X-XSRF-TOKEN": self.csrf_token,
                "X-Requested-With": "XMLHttpRequest",
                "Referer": f"{PUBLIC_SERVICE_URL}/tthc/login",
            },
        )
        result = login_response.json()
        if str(result.get("status")) not in {"200", "201"}:
            raise RuntimeError(f"Đăng nhập DVC thất bại: {result}")

    def get_payment_request_document(self) -> requests.Response:
        self.request(
            "05_other_services", self.dvc, "GET", f"{PUBLIC_SERVICE_URL}/tthc/dich-vu-khac",
            headers={"Referer": f"{PUBLIC_SERVICE_URL}/tthc/home"},
        )
        response = self.request(
            "06_sso_redirect", self.dvc, "POST",
            f"{PUBLIC_SERVICE_URL}/tthc/sso/redirect-to-service?module=330410",
            data="", headers={
                "Referer": f"{PUBLIC_SERVICE_URL}/tthc/dich-vu-khac",
                "X-Requested-With": "XMLHttpRequest", "X-XSRF-TOKEN": self.csrf_token,
            },
        )
        redirect_url = response.text.strip().strip('"')
        if redirect_url.startswith("{"):
            obj = json.loads(redirect_url)
            redirect_url = obj.get("url") or obj.get("data") or ""
        if not redirect_url.startswith("http"):
            raise RuntimeError(f"DVC không trả URL SSO hợp lệ: {redirect_url!r}")

        step3 = self.request("07_etax_redirect", self.etax, "GET", redirect_url,
                             headers={"Referer": f"{PUBLIC_SERVICE_URL}/"})
        step4_url = self.normalize_etax_url(self.redirect_from(step3))
        step4 = self.request("08_etax_corp_jump", self.etax, "GET", step4_url,
                             headers={"Referer": f"{PUBLIC_SERVICE_URL}/"})
        step5_url = self.normalize_etax_url(self.redirect_from(step4))
        step5 = self.request("09_etax_establish_session", self.etax, "GET", step5_url,
                             headers={"Referer": f"{PUBLIC_SERVICE_URL}/"})
        step6_url = self.normalize_etax_url(self.redirect_from(step5))
        query = parse_qs(urlparse(step6_url).query)
        self.etax_session_id = unquote(query.get("dse_sessionId", [""])[0])
        if not self.etax_session_id:
            raise RuntimeError("Không lấy được dse_sessionId ở EstablishSession")

        step6 = self.request("10_etax_new_session", self.etax, "GET", step6_url,
                             headers={"Referer": step5_url})
        form6 = self.form_inputs(step6.text)
        self.etax_session_id = form6.get("dse_sessionId", self.etax_session_id)
        step7 = self.request("11_etax_sso_login", self.etax, "POST", f"{ETAX_URL}/etaxnnt/Request",
                             data=form6, headers={"Referer": step6_url, "Origin": ETAX_URL})
        form7 = self.form_inputs(step7.text)
        self.etax_session_id = form7.get("dse_sessionId", self.etax_session_id)
        self.request("12_etax_main_page", self.etax, "POST", f"{ETAX_URL}/etaxnnt/Request",
                     data=form7, headers={"Referer": f"{ETAX_URL}/etaxnnt/Request", "Origin": ETAX_URL})

        query_page = self.request("13_payment_query_page", self.etax, "POST", f"{ETAX_URL}/etaxnnt/Request",
            data={"dse_sessionId": self.etax_session_id, "dse_applicationId": "-1", "dse_pageId": "4",
                  "dse_operationName": "corpQueryTaxProc", "dse_errorPage": "error_page.jsp",
                  "dse_processorState": "initial", "dse_nextEventName": "start"},
            headers={"Referer": f"{ETAX_URL}/etaxnnt/Request", "Origin": ETAX_URL})
        page_form = self.form_inputs(query_page.text)
        self.processor_id = page_form.get("dse_processorId", "")
        self.query_page_id = page_form.get("dse_pageId", "10")
        self.etax_session_id = page_form.get("dse_sessionId", self.etax_session_id)
        if not self.processor_id:
            raise RuntimeError("Không tìm thấy dse_processorId ở trang tra cứu GNT")

        return self.request("14_payment_query", self.etax, "POST", f"{ETAX_URL}/etaxnnt/Request",
            data={"dse_sessionId": self.etax_session_id, "dse_applicationId": "-1",
                  "dse_operationName": "corpQueryTaxProc", "dse_pageId": self.query_page_id,
                  "dse_processorState": "viewQueryPage", "dse_processorId": self.processor_id,
                  "dse_errorPage": "/etax/query_tax_information.jsp", "dse_nextEventName": "query",
                  "pn": "1", "sct": "", "ctuId": "", "soGnt": "", "idBke": "", "type_tax": "01",
                  "ma_giao_dich": "", "so_ctu_nh": "", "so_gnt": "",
                  "ngay_lap_tu_ngay": FROM_DATE, "ngay_lap_den_ngay": TO_DATE,
                  "ngay_gui_tu_ngay": "", "ngay_gui_den_ngay": "", "ngay_nop_tu_ngay": "",
                  "ngay_nop_den_ngay": "", "ma_nhang": "", "so_tk": "", "nguyen_te": "",
                  "hthuc_nop": "", "tong_tien_nt_tu": "", "tong_tien_nt_den": "", "trang_thai": ""},
            headers={"Referer": f"{ETAX_URL}/etaxnnt/Request", "Origin": ETAX_URL})


def main() -> int:
    if not USERNAME or not PASSWORD:
        print("Hãy điền USERNAME và PASSWORD ở đầu file trước khi chạy.", file=sys.stderr)
        return 2
    DEBUG_DIR.mkdir(exist_ok=True)
    try:
        flow = FlowDebugger()
        result = flow.sign_in_dvc()
        flow.get_payment_request_document()
        print(f"\nHoàn tất. Xem request/response (đã che dữ liệu nhạy cảm) tại: {DEBUG_DIR.resolve()}")
        return 0
    except (requests.RequestException, ValueError, RuntimeError) as exc:
        print(f"\nLuồng dừng vì lỗi: {exc}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
