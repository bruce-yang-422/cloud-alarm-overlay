"""Overwrite the existing Google Drive version.json only when its content changed.

Run from the project root: python scripts/sync_version_json.py
The service account must have edit access to the existing Drive file.
"""

import argparse
import json
import re
from pathlib import Path
from urllib.parse import urlparse

from google.auth.transport.requests import AuthorizedSession
from google.oauth2.service_account import Credentials


ROOT = Path(__file__).resolve().parent.parent
DEFAULT_MANIFEST = ROOT / "version.json"
DEFAULT_KEY = ROOT / "google_key_linkaishi0514.json"
DEFAULT_FILE_ID = "1-8bIzVhXixepf5TW0PvjrBMxkWJVTTVJ"
DRIVE_SCOPE = "https://www.googleapis.com/auth/drive"


def read_manifest(path: Path) -> bytes:
    content = path.read_bytes()
    manifest = json.loads(content.decode("utf-8"))
    if not isinstance(manifest, dict):
        raise ValueError("version.json 必須是 JSON 物件。")
    version = manifest.get("latestVersion")
    if not isinstance(version, str) or not re.fullmatch(r"\d+\.\d+\.\d+(?:\.\d+)?", version):
        raise ValueError("latestVersion 必須是數字版本，例如 1.0.0。")
    download_url = manifest.get("downloadUrl")
    if not isinstance(download_url, str) or "REPLACE_WITH_" in download_url:
        raise ValueError("請先填入真正的安裝檔 downloadUrl。")
    parsed = urlparse(download_url)
    if parsed.scheme != "https" or not parsed.netloc or parsed.username or parsed.password:
        raise ValueError("downloadUrl 必須是公開 HTTPS 連結。")
    if not isinstance(manifest.get("releaseNote"), str):
        raise ValueError("releaseNote 必須是文字。")
    return content


def sync(manifest_path: Path, key_path: Path, file_id: str) -> bool:
    content = read_manifest(manifest_path)
    credentials = Credentials.from_service_account_file(str(key_path), scopes=[DRIVE_SCOPE])
    session = AuthorizedSession(credentials)
    file_url = f"https://www.googleapis.com/drive/v3/files/{file_id}"
    metadata = session.get(
        file_url,
        params={"fields": "id,name,mimeType,capabilities(canEdit)", "supportsAllDrives": "true"},
        timeout=20,
    )
    metadata.raise_for_status()
    info = metadata.json()
    if info.get("id") != file_id or info.get("name") != "version.json":
        raise ValueError("Drive 檔案不是預期的 version.json，已停止上傳。")
    if not info.get("capabilities", {}).get("canEdit"):
        raise PermissionError("服務帳戶沒有編輯 version.json 的權限。")
    if info.get("mimeType", "").startswith("application/vnd.google-apps."):
        raise ValueError("Drive 上的 version.json 必須是原始 JSON 檔案，不能是 Google 文件。")

    current = session.get(file_url, params={"alt": "media", "supportsAllDrives": "true"}, timeout=20)
    current.raise_for_status()
    if current.content == content:
        print("version.json unchanged; upload skipped; Drive link preserved.")
        return False

    updated = session.patch(
        f"https://www.googleapis.com/upload/drive/v3/files/{file_id}",
        params={"uploadType": "media", "fields": "id,name", "supportsAllDrives": "true"},
        data=content,
        headers={"Content-Type": "application/json; charset=utf-8"},
        timeout=30,
    )
    updated.raise_for_status()
    if updated.json().get("id") != file_id:
        raise RuntimeError("上傳後的 Drive 檔案 ID 與原檔不同。")
    verified = session.get(file_url, params={"alt": "media", "supportsAllDrives": "true"}, timeout=20)
    verified.raise_for_status()
    if verified.content != content:
        raise RuntimeError("上傳後的 version.json 內容驗證失敗。")
    print("version.json uploaded and verified; Drive link preserved.")
    return True


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--manifest", type=Path, default=DEFAULT_MANIFEST, help="本機 version.json 路徑")
    parser.add_argument("--key", type=Path, default=DEFAULT_KEY, help="Google 服務帳戶金鑰路徑")
    parser.add_argument("--file-id", default=DEFAULT_FILE_ID, help="既有 Drive version.json 檔案 ID")
    args = parser.parse_args()
    sync(args.manifest, args.key, args.file_id)


if __name__ == "__main__":
    main()
