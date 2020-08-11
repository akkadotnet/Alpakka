# Based on https://github.com/spring2/dockerfiles/blob/master/rabbitmq/Dockerfile

FROM mcr.microsoft.com/windows/servercore:ltsc2019

SHELL ["powershell", "-Command", "$ErrorActionPreference = 'Stop'; $ProgressPreference = 'SilentlyContinue';"]
ARG VERSION
ENV VERSION=$VERSION
ENV chocolateyUseWindowsCompression false

RUN [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12; \
    iex ((new-object net.webclient).DownloadString('https://chocolatey.org/install.ps1')); \
	choco install -y curl;

RUN choco install -y erlang --version 22.3
ENV ERLANG_HOME="C:\Program Files\erl10.7"
ENV ERLANG_SERVICE_MANAGER_PATH="C:\Program Files\erl10.7\erts-10.7\bin"

RUN [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12; \
	curl ('https://github.com/rabbitmq/rabbitmq-server/releases/download/v3.8.6/rabbitmq-server-windows-3.8.6.zip') -o $env:TEMP\rabbitmq-server.zip; \
    Expand-Archive "$env:TEMP\rabbitmq-server.zip" -DestinationPath 'C:\RabbitMQ'; \
    del "$env:TEMP\rabbitmq-server.zip";
ENV RABBITMQ_SERVER="C:\RabbitMQ\rabbitmq_server-3.8.6"

ENV RABBITMQ_CONFIG_FILE="c:\rabbitmq.conf"
COPY rabbitmq.conf C:/
COPY rabbitmq.conf C:/Users/ContainerAdministrator/AppData/Roaming/RabbitMQ/
COPY enabled_plugins C:/Users/ContainerAdministrator/AppData/Roaming/RabbitMQ/

EXPOSE 4369 5672 5671 15672

WORKDIR C:/RabbitMQ/rabbitmq_server-3.8.6/sbin
CMD .\rabbitmq-server.bat