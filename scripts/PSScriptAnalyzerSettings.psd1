@{
    # T-F150: CI gate for scripts/*.ps1 (see .github/workflows/build.yml's "lint-ps1" job).
    # PSAvoidUsingWriteHost is excluded here, not left as unaddressed noise: Deploy.ps1/
    # CI-Build-Msix.ps1/Publish-Cli.ps1/Setup-DevCert.ps1/Setup-StoreCert.ps1 are interactive
    # operator-facing scripts, not reusable modules - Write-Host's colored console feedback is the
    # correct tool here, not an anti-pattern to fix. Reviewed and documented, per
    # docs/CONVENTIONS.md's "Static-Analysis Won't-Fix Conventions" section. Any other finding
    # (including Warning-severity) fails CI.
    ExcludeRules = @('PSAvoidUsingWriteHost')
}
