# 台灣天氣地點選單與生活區參考位置

`TaiwanWeatherLocations.json` 包含 22 縣市、368 鄉鎮市區、3 碼郵遞區號及區域預報參考位置。選單與搜尋離線運作；只有儲存後的天氣更新會呼叫 Open-Meteo。

## 資料來源與授權

- 行政區名稱／代碼與邊界驗證：內政部國土測繪中心「鄉鎮市區界線（TWD97 經緯度）」，含瑪家鄉修正圖層。https://maps.nlsc.gov.tw/pro/download.jsp ，政府資料開放授權條款第 1 版：https://data.gov.tw/license 。
- 郵遞區號：中華郵政「3碼郵遞區號與行政區中心點經緯度對照表」，僅使用行政區名與前 3 碼欄位，不採用其中的幾何中心座標。https://www.post.gov.tw/post/internet/Download/all_list.jsp?ID=2201 。官方 371 筆資料中，只納入本選單對應的 368 個行政區。
- 公所／行政中心位置：© OpenStreetMap contributors，ODbL 1.0，https://www.openstreetmap.org/copyright ，https://opendatacommons.org/licenses/odbl/1-0/ 。本次採用 344 筆。
- 聚落位置：GeoNames，CC BY，https://www.geonames.org/export/ ，https://download.geonames.org/export/dump/TW.zip 。本次採用 24 筆。

本 JSON 是經篩選、比對、座標取位的衍生資料庫，依 ODbL 1.0 提供；各筆 `ReferenceSource` 保留原始地點連結。資料庫授權不變更本專案程式碼的 MIT 授權。完整 JSON 與本說明亦隨應用程式放在 `Data/`，供取得與核對。

## 位置選擇

優先選取落在該行政區內、名稱對應的公所或行政中心；排除舊址、站牌、停車場等同名地標。缺少公所資料時，以 GeoNames 同名聚落、行政中心或較高人口的候選點作參考。每筆位置均通過行政區邊界檢查，不再回退到山區幾何中心。

白沙鄉採赤崁（GeoNames 大赤崁）聚落，公所地址依 https://www.penghu.gov.tw/Tbaisha/ch/index.jsp ；西嶼鄉採赤馬聚落，公所地址依 https://www.penghu.gov.tw/Thsiyu/ch/index.jsp 。它們代表公所所在的生活聚落，不宣稱是公所建物精確位置。

座標並非氣象站或全區平均，不保證山區、沿海或附屬離島有相同天氣。選單顯示 `ReferenceName` 供辨識。TWD97 邊界以 WGS84 近似作天氣用途的包含檢查，不供測量或地籍認定。

## 搜尋規則

- 台／臺同義、全形數字轉半形、縣市與行政區關鍵字；可用空白分隔多個條件。
- 1～2 位數可縮小郵遞區號候選；完整 3 碼、5 碼或 6 碼只依前 3 碼對應。允許 `220-001` 貼上格式。
- 同名區、共用郵遞區號均保留多筆供選擇，例如新竹市 `300` 三區、嘉義市 `600` 兩區。不做街道或完整郵遞區號有效性驗證。
- 搜尋不自動替換已選地點；點選結果，或單一結果時按 Enter，才同步雙層選單。仍需按儲存才套用。
- 舊版同名選單項目可帶入新版生活區座標，按儲存後生效；舊搜尋式地名不猜測對應、不自動改寫。

## 更新方法

開發用 Python 需 `pyshp`、`shapely`。應用程式不依賴 Python。

先下載中華郵政 XML：
`https://www.post.gov.tw/post/download/1050812_%E8%A1%8C%E6%94%BF%E5%8D%80%E7%B6%93%E7%B7%AF%E5%BA%A6%28toPost%29.xml`

再透過 Overpass 取得公所 JSON；查詢（UTF-8）：
```text
[out:json][timeout:180];nwr["name"~"公所|行政中心"](21.8,118,26.5,122.1);out center tags;
```

執行：
```powershell
.\.venv\Scripts\python.exe scripts/update_weather_locations.py --postal-file postal.xml --townhalls-file offices.json
```

腳本下載官方邊界及 GeoNames，檢查 368 個唯一行政區與郵遞區號、座標範圍；無法找到聚落或公所時會停止，不以幾何中心補值。更新後須審查來源、選點及測試，不能僅依自動候選保證地圖資料即時正確。

2026-09-24 取得資料；OpenStreetMap 回應的底圖時間：`2026-07-24T11:04:51Z`。資料來源可能有更新落差，不宣稱即時行政機關位置。

SHA-256：
- 公所 JSON：`22ffabe42c051b242c2cefaed26718ae24e29d72507cea99bd66454fd2348bef`
- 邊界 ZIP：`e028e5a750eee48cf7913330655e5e5c5bb1f176868fbd0afdfc661fca60557c`
- 郵遞 XML：`5bdc716df9170166b3e62183a38b69851ca1d95208964485a3d8cc291316a8d7`
- GeoNames ZIP：`51a51f5d876aab0035cd0202f8ebd51b74ca0380ba0044fec27dc93334f5fe01`

天氣服務：https://open-meteo.com/en/docs ，使用代表點的網格預報，不是中央氣象署鄉鎮預報。
