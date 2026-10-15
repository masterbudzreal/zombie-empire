"""Minimal NBT read/write (gzip level.dat / playerdata). Tags kept as (type, value)."""
import gzip, struct, sys, json

FMT = {1: ">b", 2: ">h", 3: ">i", 4: ">q", 5: ">f", 6: ">d"}


class R:
    def __init__(s, b): s.b, s.p = b, 0
    def take(s, n): v = s.b[s.p:s.p + n]; s.p += n; return v
    def num(s, f): return struct.unpack(f, s.take(struct.calcsize(f)))[0]
    def str(s): return s.take(s.num(">H")).decode("utf-8", "surrogateescape")

    def payload(s, t):
        if t in FMT: return s.num(FMT[t])
        if t == 7: return s.take(s.num(">i"))
        if t == 8: return s.str()
        if t == 9:
            et, n = s.num(">b"), s.num(">i")
            return (et, [s.payload(et) for _ in range(n)])
        if t == 10:
            d = {}
            while True:
                tt = s.num(">b")
                if tt == 0: return d
                k = s.str(); d[k] = (tt, s.payload(tt))
        if t == 11: n = s.num(">i"); return list(struct.unpack(">%di" % n, s.take(4 * n)))
        if t == 12: n = s.num(">i"); return list(struct.unpack(">%dq" % n, s.take(8 * n)))
        raise ValueError(t)


def wpayload(o, t, v):
    if t in FMT: o += struct.pack(FMT[t], v)
    elif t == 7: o += struct.pack(">i", len(v)) + v
    elif t == 8: e = v.encode("utf-8", "surrogateescape"); o += struct.pack(">H", len(e)) + e
    elif t == 9:
        et, items = v; o += struct.pack(">bi", et, len(items))
        for i in items: wpayload(o, et, i)
    elif t == 10:
        for k, (tt, vv) in v.items():
            e = k.encode("utf-8", "surrogateescape")
            o += struct.pack(">bH", tt, len(e)) + e; wpayload(o, tt, vv)
        o += b"\0"
    elif t == 11: o += struct.pack(">i%di" % len(v), len(v), *v)
    elif t == 12: o += struct.pack(">i%dq" % len(v), len(v), *v)


def load(path):
    r = R(gzip.open(path).read()); t = r.num(">b"); name = r.str()
    return name, r.payload(t)


def save(path, name, root):
    o = bytearray(); e = name.encode(); o += struct.pack(">bH", 10, len(e)) + e
    wpayload(o, 10, root)
    with gzip.open(path, "wb") as f: f.write(bytes(o))
