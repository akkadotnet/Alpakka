---
uid: Testing.md
title: Running Alpakka Test Suite
---

# How to run the Alpakka unit test suite

Since Alpakka is a collection of adapters for Akka.Streams, the Alpakka test suite relies on 3rd party software for it to pass. To run all the tests successfully, developer would need to either install these 3rd party softwares, or use a prepared docker image with the relevant software installed.
Currently, the AMQP and Azure test suite requires 3rd party softwares to run.

# AMQP

Right now, we rely on RabbitMQ as our AMQP test platform. To test the AMQP suite, you would need to install RabbitMQ locally, use the pre-made docker image, or build the docker image yourself. 
The test suite expects a working AMQP broker with these default settings:
- user: guest
- password: guest
- vhost: /
- user tag: administrator
- permission: .*, .*, .*
- loopback user: []
- strict ssl protocol mode: false

## Installing AMQP

- Install RabbitMQ either using Chocolatey or the binary installer by following the instructions [here](https://www.rabbitmq.com/install-windows.html).
- Copy `support\dockerfiles\AMQP\Windows\rabbitmq.conf` file to the `%APPDATA%\RabbitMQ\` folder
- Copy `support\dockerfiles\AMQP\Windows\enabled_plugins` file to the `%APPDATA%\RabbitMQ\` folder
- Restart RabbitMQ server service or start the RabbitMQ server
- Edit the `environment.json` file inside the `Akka.Streams.Amqp.Tests` project. Set `ALPAKKA_AMQP_TEST_USEDOCKER` to false.

## Using a pre-built Docker container

- Edit the `environment.json` file inside the `Akka.Streams.Amqp.Tests` project. Set `ALPAKKA_AMQP_TEST_USEDOCKER` to true.
- A pre-built docker container image can be obtained by running:
  `docker pull akkadotnet/rabbitmq:latest`
- The docker image will be automatically pulled the first time you run the AMQP test suite, if you do not have a local copy.

## Using the pre-built Docker container as an application

You can run the pre-built docker container as a background application so that it does not have to be loaded/unloaded every time tests are run:
- Edit the `environment.json` file inside the `Akka.Streams.Amqp.Tests` project. Set `ALPAKKA_AMQP_TEST_USEDOCKER` to false.
- Pull and run the image by running:
  `docker pull akkadotnet/rabbitmq:latest`
  `docker run -d -p 4369:4369 -p 5672:5672 -p 5671:5671 -p 15672:15672 akkadotnet/rabbitmq:latest`
- Wait until the application started and you can start testing.

# Azure

## Installing Azure Storage Emulator
- The Azure test suite depends on Azure Storage Emulator to work on Windows, and Azurite to work on Linux.
- To run the test suite against a Docker container, Edit the `environment.json` file inside the `Akka.Streams.Azure.StorageQueue.Tests` project. Set `ALPAKKA_AZURE_TEST_USEDOCKER` to false.
- **Azure Storage Emulator**: 
  - If you're using Visual Studio and have the Azure SDK installed, chances are, you already have Azure Storage Emulator installed. You can download the standalone installer [here](https://go.microsoft.com/fwlink/?linkid=717179&clcid=0x409)
  - Start the emulator before you run the tests.
- **Azurite**: 
  - You can install Azurite using npm by usiing this command: `npm install -g azurite`
  - Start the emulator before you run the tests.

## Using a pre-built Docker container
- The test suite can also be run against docker containers. 
- To run the test suite against a Docker container, Edit the `environment.json` file inside the `Akka.Streams.Azure.StorageQueue.Tests` project. Set `ALPAKKA_AZURE_TEST_USEDOCKER` to true.
- The containers used are `akkadotnet/azure-storage-emulator:latest` for windows and `mcr.microsoft.com/azure-storage/azurite` for linux.

## Using the pre-built Docker container as an application

You can run the pre-built docker container as a background application so that it does not have to be loaded/unloaded every time tests are run:
- Edit the `environment.json` file inside the `Akka.Streams.Azure.StorageQueue.Tests` project. Set `ALPAKKA_AZURE_TEST_USEDOCKER` to false.
- Pull and run the image by running:
  `docker pull akkadotnet/azure-storage-emulator:latest`
  `docker run -d -p 10000:10000 -p 10001:10001 -p 10002:10002 akkadotnet/azure-storage-emulator:latest`
- Wait until the application started and you can start testing.
