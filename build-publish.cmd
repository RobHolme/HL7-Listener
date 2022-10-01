del .\bin\publish\net6.0\win-x64\
del .\bin\publish\net6.0\linux-x64\
del  .\bin\publish\net4.8\
dotnet publish --configuration release --framework net6.0 --runtime win-x64 --self-contained -p:PublishSingleFile=true --output .\bin\publish\net6.0\win-x64\ 
dotnet publish --configuration release --framework net6.0 --runtime linux-x64 --self-contained -p:PublishSingleFile=true --output .\bin\publish\net6.0\linux-x64\
dotnet publish --configuration release --framework net48 --output  .\bin\publish\net4.8\