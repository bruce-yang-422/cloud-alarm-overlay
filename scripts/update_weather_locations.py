"""Build offline county/district/postcode menus and inhabited reference locations.
Prerequisites: pyshp, shapely. Inputs: official postal XML and OSM townhall JSON.
Usage: python scripts/update_weather_locations.py --postal-file PATH --townhalls-file PATH
Missing references are reported instead of falling back to polygon centres.
"""
import argparse
import hashlib
import json
import re
import xml.etree.ElementTree as ET
from io import BytesIO
from pathlib import Path
from urllib.parse import quote
from urllib.request import urlopen
from zipfile import ZipFile
import shapefile
from shapely.geometry import shape, Point

ROOT = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--postal-file", type=Path, required=True)
parser.add_argument("--townhalls-file", type=Path, required=True)
parser.add_argument("--review-output", type=Path)
args = parser.parse_args()
normalize = lambda s: s.replace("台", "臺")
postal_raw = args.postal_file.read_bytes()
postal = {normalize(e.findtext("行政區名")): e.findtext("_x0033_碼郵遞區號") for e in ET.fromstring(postal_raw)}
osm = json.loads(args.townhalls_file.read_text(encoding="utf-8-sig"))
if osm.get("remark"): raise ValueError(osm["remark"])
raw = urlopen("https://maps.nlsc.gov.tw/download/" + quote("鄉鎮市區界線(TWD97經緯度).zip"), timeout=60).read()
archive = ZipFile(BytesIO(raw)); rows = {}; geometries = {}; english = {}
for filename in sorted((n for n in archive.namelist() if n.endswith(".shp")), key=lambda n: 0 if "TOWN_MOI" in n else 1):
    reader = shapefile.Reader(shp=BytesIO(archive.read(filename)), dbf=BytesIO(archive.read(filename[:-4] + ".dbf")), encoding="utf-8")
    for entry in reader.iterShapeRecords():
        r = entry.record.as_dict(); code = r["TOWNCODE"]
        rows[code] = dict(Code=code, County=r["COUNTYNAME"], District=r["TOWNNAME"], PostalCode=postal[normalize(r["COUNTYNAME"] + r["TOWNNAME"])] )
        english[code] = r["TOWNENG"].split(" Township")[0].split(" District")[0].split(" City")[0].split(" Town")[0]
        geometries[code] = shape(entry.shape.__geo_interface__)
geonames_raw = urlopen("https://download.geonames.org/export/dump/TW.zip", timeout=60).read()
places = [r for line in ZipFile(BytesIO(geonames_raw)).read("TW.txt").decode().splitlines() if (r := line.split("\t"))[6] == "P" and r[7] not in ("PPLH", "PPLQ", "PPLW")]
missing = []
for code, row in rows.items():
    district = row["District"]; geometry = geometries[code]
    offices = []
    # Public office addresses establish these settlement aliases (see Data/README.md).
    seat_aliases = {"10016030": "大赤崁", "10016040": "赤馬"}
    for e in osm["elements"]:
        tags = e.get("tags", {}); name = normalize(tags.get("name", tags.get("name:zh", "")))
        if not any(term in name for term in ("公所", "行政中心")) or any(term in name for term in ("舊", "原址", "已遷", "代表", "停用", "停車場", "站牌", "公所前")): continue
        if tags.get("highway") == "bus_stop" or tags.get("public_transport") in ("platform", "stop_position"): continue
        pos = e.get("center", e)
        if not geometry.covers(Point(pos["lon"], pos["lat"])): continue
        offices.append((district + "公所" in name, e["type"] == "node", e))
    if offices:
        e = sorted(offices, key=lambda item: (item[0], item[1], -item[2]["id"]), reverse=True)[0][2]
        pos = e.get("center", e)
        row.update(Latitude=round(pos["lat"],6), Longitude=round(pos["lon"],6), ReferenceName=e["tags"].get("name",e["tags"].get("name:zh", district+"公所附近")), ReferenceSource="https://www.openstreetmap.org/" + e["type"] + "/" + str(e["id"]))
    else:
        names = {normalize(district), normalize(district[:-1]), english[code]}
        if code in seat_aliases: names.add(seat_aliases[code])
        candidates = [p for p in places if names.intersection(([p[1], p[2]] + normalize(p[3]).split(","))) and geometry.covers(Point(float(p[5]),float(p[4])))]
        if not candidates:
            missing.append(row["County"]+district); continue
        p = sorted(candidates,key=lambda p:(p[7].startswith("PPLA"), int(p[14]), district in p[3].split(","), -int(p[0])),reverse=True)[0]
        row.update(Latitude=float(p[4]),Longitude=float(p[5]),ReferenceName=(seat_aliases.get(code, district))+"聚落",ReferenceSource="https://www.geonames.org/"+p[0])
values = sorted(rows.values(),key=lambda row:row["Code"])
print("Missing inhabited references:",missing)
print("Sources:", {source:sum(source in r.get("ReferenceSource","") for r in values) for source in ("openstreetmap", "geonames")})
if args.review_output: args.review_output.write_text(json.dumps(values,ensure_ascii=False,indent=2)+"\n",encoding="utf-8")
if missing: raise SystemExit(1)
assert len(values) == 368 and len({r["County"] for r in values}) == 22
assert all(re.fullmatch(r"[0-9]{3}",r["PostalCode"]) and 21<r["Latitude"]<27 and 118<r["Longitude"]<123 for r in values)
(ROOT / "src/CloudAlarmOverlay.Core/Data/TaiwanWeatherLocations.json").write_text(json.dumps(values,ensure_ascii=False,indent=2)+"\n",encoding="utf-8")
print("SHA256", {"offices":hashlib.sha256(args.townhalls_file.read_bytes()).hexdigest(),"boundaries":hashlib.sha256(raw).hexdigest(),"postal":hashlib.sha256(postal_raw).hexdigest(),"geonames":hashlib.sha256(geonames_raw).hexdigest()})
