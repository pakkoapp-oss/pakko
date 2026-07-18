# deploy/ — Group Policy ADMX/ADML Templates

This folder contains the Group Policy administrative template for Pakko's four policy keys
(`EnforceMOTW`, `AllowedFormats`, `BlockedFormats`, `DisableTarExtraction`), read from
`HKLM\Software\Policies\Pakko\`. See [`POLICIES.md`](../POLICIES.md) at the repo root for what
each key does and why; this file only covers installing the templates themselves.

```
deploy/
├── Pakko.admx        ← policy definitions (language-neutral)
├── en-US/
│   └── Pakko.adml     ← English display strings/explanations for the policies above
└── README.md          ← you are here
```

Nothing under `deploy/` is packaged, signed, or installed by `Deploy.ps1` — these files are a
sysadmin-facing artifact, entirely separate from the MSIX build/install pipeline.

## Local Group Policy (single machine, e.g. testing)

Run as Administrator (writing to `%SystemRoot%\PolicyDefinitions` requires elevation):

```powershell
Copy-Item deploy\Pakko.admx "$env:SystemRoot\PolicyDefinitions\"
New-Item -ItemType Directory -Force "$env:SystemRoot\PolicyDefinitions\en-US" | Out-Null
Copy-Item deploy\en-US\Pakko.adml "$env:SystemRoot\PolicyDefinitions\en-US\"
```

Then open `gpedit.msc` → **Computer Configuration → Administrative Templates → Pakko**. All four
policies should appear with no XML parse errors. If a previously open Group Policy Editor window
doesn't show the new category, close and reopen it — it only rescans `PolicyDefinitions` on open.

To remove: delete the two copied files and restart `gpedit.msc`.

## Domain deployment (Active Directory)

Copy the same two files into the domain's Central Store instead, so every domain controller and
admin workstation picks them up automatically — no per-machine copy needed:

```powershell
Copy-Item deploy\Pakko.admx "\\<domain>\SYSVOL\<domain>\Policies\PolicyDefinitions\"
Copy-Item deploy\en-US\Pakko.adml "\\<domain>\SYSVOL\<domain>\Policies\PolicyDefinitions\en-US\"
```

The Central Store must already exist (created the first time *any* vendor ADMX is centrally
deployed on that domain) — see Microsoft's own
["How to create and manage the Central Store"](https://learn.microsoft.com/en-us/troubleshoot/windows-server/group-policy/create-and-manage-central-store)
if this domain doesn't have one yet.

## Without the ADMX/ADML templates

The templates are a convenience UI over the registry — Pakko itself only ever reads the raw
`HKLM\Software\Policies\Pakko\` values, regardless of how they were set. Any of the following work
identically without installing anything from this folder:

- `reg add HKLM\Software\Policies\Pakko /v DisableTarExtraction /t REG_DWORD /d 1`
- `New-ItemProperty -Path 'HKLM:\Software\Policies\Pakko' -Name DisableTarExtraction -PropertyType DWord -Value 1`
- An existing GPO "Registry" preference item pointed at the same path/value names.
