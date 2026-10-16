# Kreo 2.4 GHz receiver protocol

Sora talks to the Swarm 75 through its 2.4 GHz receiver (`3554:FA09`) using the receiver's vendor collection: usage page `FF02`, usage `2`, 20-byte input and output reports with report ID `0x13`.

## Packet

| Byte | Request | Reply / acknowledgement |
|---|---|---|
| 0 | `0x13` | `0x13` |
| 1 | command | command echo |
| 2 | packet count | packet count; bit 7 set = error |
| 3 | packet index | packet index |
| 4 | `(selector << 4) \| data length` | data length (low nibble) |
| 5–18 | up to 14 data bytes | up to 14 data bytes |
| 19 | sum of bytes 0–18 (mod 256) | sum of bytes 0–18 |

A write sends the block in 14-byte packets. Each packet is acknowledged by a report that echoes it; the next packet is sent only after the acknowledgement. A read sends one request packet with the read command and receives the block as numbered packets.

## Commands

| Data | Write | Read | Block | Status in Sora |
|---|---|---|---|---|
| Lighting | `0x04` | `0x44` | 128 bytes, same layout as the wired block, `5A A5` at 126 | read and write verified on a Swarm 75 |
| Key map, layer in selector | `0x01` | `0x41` | 504 bytes (126 keys × 4) | read verified; write not yet enabled |
| Effect palette | `0x09` | `0x49` | 490 bytes; first 420 match the wired palette | read verified; write not yet enabled |
| Battery | — | `0x4A` | 2 bytes, byte 0 = percent | read |
| Per-key colors | `0x06` | none | sent without acknowledgement | not supported (cannot be read back) |

The wired configuration channel (`258A:010C`, 520-byte feature reports) is unchanged; Sora prefers it whenever the cable is connected.

## Verifying writes

`tests/Sora.Checks` has two hardware checks that back up, change, read back and restore:

```
dotnet run --project tests/Sora.Checks -- --keyboard-wireless-verify-lighting --output artifacts/kreo-wireless-lighting.json
dotnet run --project tests/Sora.Checks -- --keyboard-wireless-verify-blocks   --output artifacts/kreo-wireless-blocks.json
```

`--keyboard-wireless-probe` performs reads only.
