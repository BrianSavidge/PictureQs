$ErrorActionPreference = "Stop"
Push-Location $PSScriptRoot
try {
    npx --yes esbuild@0.25.10 .\viewer.js --bundle --minify --alias:three=./build/three.module.js --outfile=.\viewer.bundle.js
    if ($LASTEXITCODE -ne 0) {
        throw "The skeleton viewer bundle could not be generated."
    }
}
finally {
    Pop-Location
}
