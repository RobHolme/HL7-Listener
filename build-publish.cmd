dotnet publish --configuration release --framework net6.0 --runtime win-x64 --self-contained -p:PublishSingleFile=true --output .\bin\publish\win-x64\ 
dotnet publish --configuration release --framework net6.0 --runtime linux-x64 --self-contained -p:PublishSingleFile=true --output .\bin\publish\linux-x64\
