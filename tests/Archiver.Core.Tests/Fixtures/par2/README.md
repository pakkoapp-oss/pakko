# PAR2 golden sets (T-F275)

Written by par2cmdline 1.4.0 (`scripts/Get-Par2Oracles.ps1`) over files whose byte `i` is
`(i * 31) ^ (i >> 7)` (low 8 bits), the same bytes `Par2TestData.Content` produces:

| File | Length | Command |
|---|---|---|
| `tiny.bin` | 22 | `par2 c -s4 -c3 -n1 tiny.bin.par2 tiny.bin` |
| `data.bin` | 300001 | `par2 c -s4096 -c4 -n1 data.bin.par2 data.bin` |
| `архів.zip` | 5000 | `par2 c -s512 -c2 -n1 архів.zip.par2 архів.zip` |

`Par2CreatorTests` compares Pakko's packets with these, except the Creator packet. The data files
themselves are not kept; the tests write them again. Output of a GPL program is not GPL.
