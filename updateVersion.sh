#! /bin/bash

BUILDVERSION=${1:-./BuildVersion/BuildVersion.exe}

$BUILDVERSION \
        --verbose \
        --namespace org.herbal3d.Ragu \
        --version $(cat VERSION) \
        --versionFile RaguOS/VersionInfo.cs \
        --assemblyInfoFile RaguOS/Properties/AssemblyInfo.cs
