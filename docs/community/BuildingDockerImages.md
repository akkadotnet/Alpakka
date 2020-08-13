---
uid: BuildingDockerImages.md
title: Building Docker Images for Testing
---

# Building docker container images for testing purposes

- Dockerfile for each image are located in `support\dockerfiles`
- Each folder should contain a simple script to build, run, and push images.
- Run `./build.ps1` to build the docker image.
- Run `./Run.ps1` to run the docker image.
- Run `./Push.ps1` to push the docker image to the container registry.