"""Drops the embedded textures from a binary FBX, keeping geometry, skin and skeleton.

Mixamo's "FBX for Unity" characters carry their textures inside the file (Jennifer: 51.7 of
53.8 MB). The trainer silhouette draws only a contour, so the textures are dead weight in the
project and in git. This rewrites the file with every Video node's Content emptied.

Usage: python strip_fbx_textures.py in.fbx out.fbx [--info]
  --info  only report what would be stripped.

A minimal reader/writer of the binary FBX node format (7.x; 64-bit offsets from 7.5).
"""
import struct, sys, zlib

MAGIC = b"Kaydara FBX Binary  \x00\x1a\x00"


def read_node(buf, pos, wide):
    if wide:
        end, nprops, plen = struct.unpack_from("<QQQ", buf, pos); pos += 24
    else:
        end, nprops, plen = struct.unpack_from("<III", buf, pos); pos += 12
    nlen = buf[pos]; pos += 1
    if end == 0:
        return None, pos
    name = buf[pos:pos + nlen]; pos += nlen
    props_raw = buf[pos:pos + plen]; pos += plen
    children = []
    while pos < end:
        child, pos = read_node(buf, pos, wide)
        if child is None:
            break
        children.append(child)
    return [name, nprops, props_raw, children], end


def write_node(node, out, offset, wide):
    name, nprops, props_raw, children = node
    hdr = 24 if wide else 12
    body = bytearray()
    start_children = offset + hdr + 1 + len(name) + len(props_raw)
    cur = start_children
    for c in children:
        b = write_node(c, None, cur, wide)
        body += b
        cur += len(b)
    if children or not props_raw:
        # sentinel (null record) after children, as FBX writers do
        body += b"\x00" * (hdr + 1)
        cur += hdr + 1
    end = cur
    head = struct.pack("<QQQ" if wide else "<III", end, nprops, len(props_raw)) + bytes([len(name)]) + name
    return bytes(head) + props_raw + bytes(body)


def parse(path):
    buf = open(path, "rb").read()
    assert buf[:23] == MAGIC, "not a binary FBX"
    version = struct.unpack_from("<I", buf, 23)[0]
    wide = version >= 7500
    pos = 27
    nodes = []
    while True:
        node, pos = read_node(buf, pos, wide)
        if node is None:
            break
        nodes.append(node)
    return buf, version, wide, nodes, pos


def walk(nodes, depth=0):
    for n in nodes:
        yield n, depth
        yield from walk(n[3], depth + 1)


def main():
    src, dst = sys.argv[1], sys.argv[2]
    buf, version, wide, nodes, pos = parse(src)
    print("version", version, "top nodes", [n[0].decode() for n in nodes])
    stripped = 0
    for n, d in walk(nodes):
        if n[0] == b"Video":
            for c in n[3]:
                if c[0] == b"Content" and len(c[2]) > 16:
                    stripped += len(c[2])
                    # one property of type R (raw) with zero length
                    c[1] = 1
                    c[2] = b"R" + struct.pack("<I", 0)
    print("stripped bytes", stripped)
    if "--info" in sys.argv:
        return
    out = bytearray(buf[:27])
    offset = 27
    for n in nodes:
        b = write_node(n, None, offset, wide)
        out += b
        offset += len(b)
    out += b"\x00" * ((25 if wide else 13))
    # footer: keep the original bytes that followed the terminating null record
    out += buf[pos:]
    open(dst, "wb").write(out)
    print("written", len(out))


if __name__ == "__main__":
    main()
