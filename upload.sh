#!/bin/bash

if [[ $# -eq 0 ]]; then
    MES="Update $(date '+%Y/%m/%d %H:%M:%S')"
else
    MES=$@
fi

dotnet publish
git add .

git commit -m "${MES}"
git push