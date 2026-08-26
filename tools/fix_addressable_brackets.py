# -*- coding: utf-8 -*-
"""Fix Addressable addresses / image paths that contain [ ] (illegal for Addressables)."""
import re
import shutil
from pathlib import Path

ROOT = Path(r"d:\game\My project")
CARDS_DIR = ROOT / "Assets" / "Resources" / "Data" / "Cards"
IMAGES_DIR = ROOT / "Assets" / "Resources_moved" / "Data" / "Images"
ADDRESSABLE_GROUP = ROOT / "Assets" / "AddressableAssetsData" / "AssetGroups" / "Default Local Group.asset"

ILLEGAL = re.compile(r"[\[\]{}]")


def sanitize_leaf(name: str) -> str:
    # Addressables forbids [ ] ; also strip other risky chars for addresses/filenames
    safe = ILLEGAL.sub("", name)
    safe = re.sub(r"[\\/:*?\"<>|]", "", safe)
    safe = re.sub(r"\s+", " ", safe).strip()
    return safe


def write_crlf(path: Path, text: str):
    normalized = text.replace("\r\n", "\n").replace("\n", "\r\n")
    path.write_bytes(normalized.encode("utf-8"))


def main():
    # 1) Find image files with brackets
    renames = []  # (old_stem, new_stem)
    for png in sorted(IMAGES_DIR.glob("*.png")):
        stem = png.stem
        if not ILLEGAL.search(stem):
            continue
        new_stem = sanitize_leaf(stem)
        if new_stem == stem:
            continue
        renames.append((stem, new_stem))
        dst = IMAGES_DIR / f"{new_stem}.png"
        if not dst.exists():
            shutil.move(str(png), str(dst))
        else:
            print(f"WARN: target exists, skip move png: {dst.name}")
        meta = IMAGES_DIR / f"{stem}.png.meta"
        meta_dst = IMAGES_DIR / f"{new_stem}.png.meta"
        if meta.exists() and not meta_dst.exists():
            shutil.move(str(meta), str(meta_dst))
        print(f"renamed image: {stem} -> {new_stem}")

    rename_map = dict(renames)

    # Also catch addresses that have brackets even if file already renamed differently
    # 2) Fix CardData imageAddress + optionally asset filenames
    card_fixes = 0
    for asset in sorted(CARDS_DIR.glob("*.asset")):
        text = asset.read_text(encoding="utf-8", errors="replace")
        m = re.search(r'^  imageAddress: "?(Data/Images/([^"\r\n]+))"?\s*$', text, re.M)
        if not m:
            continue
        leaf = m.group(2)
        if not ILLEGAL.search(leaf) and leaf not in rename_map:
            continue
        new_leaf = rename_map.get(leaf, sanitize_leaf(leaf))
        old_addr = m.group(1)
        new_addr = f"Data/Images/{new_leaf}"
        if old_addr == new_addr:
            continue
        text = text.replace(f'imageAddress: "{old_addr}"', f'imageAddress: "{new_addr}"')
        text = text.replace(f"imageAddress: {old_addr}", f'imageAddress: "{new_addr}"')
        write_crlf(asset, text)
        card_fixes += 1
        print(f"card imageAddress: {asset.name}: {leaf} -> {new_leaf}")

        # rename asset file if name contains brackets
        if ILLEGAL.search(asset.name):
            new_asset_name = sanitize_leaf(asset.stem) + ".asset"
            new_asset = asset.with_name(new_asset_name)
            meta = asset.with_suffix(".asset.meta")
            if not new_asset.exists():
                shutil.move(str(asset), str(new_asset))
                if meta.exists():
                    shutil.move(str(meta), str(new_asset.with_suffix(".asset.meta")))
                # also fix m_Name inside if needed
                t2 = new_asset.read_text(encoding="utf-8", errors="replace")
                t2 = re.sub(r"^  m_Name: .*$", f"  m_Name: {sanitize_leaf(asset.stem)}", t2, count=1, flags=re.M)
                write_crlf(new_asset, t2)
                print(f"renamed asset: {asset.name} -> {new_asset_name}")

    # 3) Fix Addressable group m_Address lines
    group_text = ADDRESSABLE_GROUP.read_text(encoding="utf-8", errors="replace")
    addr_fixes = 0

    def repl_addr(match: re.Match) -> str:
        nonlocal addr_fixes
        addr = match.group(1)
        if not ILLEGAL.search(addr) and not any(old in addr for old, _ in renames):
            return match.group(0)
        # extract leaf after Data/Images/
        if addr.startswith("Data/Images/"):
            leaf = addr[len("Data/Images/") :]
            new_leaf = rename_map.get(leaf, sanitize_leaf(leaf))
            new_addr = f"Data/Images/{new_leaf}"
        else:
            new_addr = sanitize_leaf(addr)
        if new_addr != addr:
            addr_fixes += 1
            print(f"addressable: {addr} -> {new_addr}")
            return f"    m_Address: {new_addr}"
        return match.group(0)

    new_group = re.sub(r"^    m_Address: (.+)$", repl_addr, group_text, flags=re.M)
    if new_group != group_text:
        write_crlf(ADDRESSABLE_GROUP, new_group)

    # 4) Scan remaining illegal addresses
    remaining = []
    for m in re.finditer(r"^    m_Address: (.+)$", ADDRESSABLE_GROUP.read_text(encoding="utf-8"), re.M):
        if ILLEGAL.search(m.group(1)):
            remaining.append(m.group(1))

    print("---")
    print(f"image_renames={len(renames)} card_fixes={card_fixes} address_fixes={addr_fixes}")
    print(f"remaining_illegal={remaining}")


if __name__ == "__main__":
    main()
