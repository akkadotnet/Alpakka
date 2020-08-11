docker stop azure-storage-emulator
docker rm azure-storage-emulator

docker run -d --name azure-storage-emulator arkatufus/azure-storage-emulator