# Based on https://github.com/tg123/Docker-AzureStorageEmulator

FROM mcr.microsoft.com/windows/servercore:ltsc2019

ENV LOCAL_DB_URL https://download.microsoft.com/download/9/0/7/907AD35F-9F9C-43A5-9789-52470555DB90/ENU/SqlLocalDB.msi
RUN powershell -NoProfile -Command \
        Invoke-WebRequest %LOCAL_DB_URL% -OutFile SqlLocalDB.msi;

RUN msiexec /i SqlLocalDB.msi /qn /norestart IACCEPTSQLLOCALDBLICENSETERMS=YES

ENV AZ_STOR_EMU_URL https://download.visualstudio.microsoft.com/download/pr/e9476781-1f65-40e4-b7fd-e6b49840c7de/7028682de076b2dbc1aa5f1e02ec420a/microsoftazurestorageemulator.msi

RUN powershell -NoProfile -Command \
        Invoke-WebRequest %AZ_STOR_EMU_URL% -OutFile MicrosoftAzureStorageEmulator.msi;

RUN msiexec /i MicrosoftAzureStorageEmulator.msi /qn

RUN powershell -NoProfile -Command \
        Remove-Item -Force *.msi;


RUN setx /M AZ_STOR_EMU_HOME "%ProgramFiles(x86)%\Microsoft SDKs\Azure\Storage Emulator"
RUN setx /M PATH "%PATH%;%AZ_STOR_EMU_HOME%"

WORKDIR "C:\Program Files (x86)\Microsoft SDKs\Azure\Storage Emulator"

# have to use nginx as reverse proxy, or 400 bad hostname
#RUN powershell -NoProfile -Command \
#        "(Get-Content .\AzureStorageEmulator.exe.config) -replace 'http://127.0.0.1','http://localhost' | Out-File -Encoding utf8 .\AzureStorageEmulator.exe.config"

RUN powershell -NoProfile -Command \
        "(Get-Content .\AzureStorageEmulator.exe.config) -replace 'http://127.0.0.1:10000/','http://127.0.0.1:20000/' | Out-File -Encoding utf8 .\AzureStorageEmulator.exe.config"; \
        "(Get-Content .\AzureStorageEmulator.exe.config) -replace 'http://127.0.0.1:10001/','http://127.0.0.1:20001/' | Out-File -Encoding utf8 .\AzureStorageEmulator.exe.config"; \
        "(Get-Content .\AzureStorageEmulator.exe.config) -replace 'http://127.0.0.1:10002/','http://127.0.0.1:20002/' | Out-File -Encoding utf8 .\AzureStorageEmulator.exe.config";

ADD entrypoint.cmd 'C:\entrypoint.cmd'

RUN AzureStorageEmulator.exe init

WORKDIR "C:\nginx"

ENV NGX_URL https://nginx.org/download/nginx-1.12.0.zip
RUN powershell -NoProfile -Command \
        Invoke-WebRequest %NGX_URL% -OutFile nginx.zip; \
        Expand-Archive nginx.zip . ;

RUN powershell -NoProfile -Command \
        Copy-Item nginx-*\*.exe . ; \
        Remove-Item -Recurse nginx-* ; \        
        Remove-Item -Force nginx.zip;

RUN powershell -NoProfile -Command \
        mkdir logs ; \
        mkdir temp ;

ADD nginx.conf 'conf\nginx.conf'

EXPOSE 10000 10001 10002

ENTRYPOINT C:\entrypoint.cmd
CMD nginx.exe