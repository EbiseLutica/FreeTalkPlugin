#!/bin/sh

dotnet publish
git add .
git commit -m "Update $(date '+%Y/%m/%d %H:%M:%S')"
git push