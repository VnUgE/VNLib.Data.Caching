#Copyright (c) Vaughn Nugent
#Licensed under the GNU AGPL V3.0

#use plain ubuntu latest to build native libraries in
FROM ubuntu:22.04 as native-cont

#install public libs and build tools
RUN apt update \
 && apt install -y build-essential cmake git

WORKDIR /build

#download and install task (t its easier an faster without npm)
ADD --checksum=sha256:d4ea84524cc4e0969e84f063fc99b9aba2090ec259c00a2e12d9ed1a182cf6af \
  https://github.com/go-task/task/releases/download/v3.39.2/task_linux_amd64.deb \
  task_linux_amd64.deb

RUN dpkg -i task_linux_amd64.deb
 
#include local artifacts
COPY app/ .

#build internal libraries and copy the libraries to the /lib output directory
RUN task setup

#APP CONTAINER
#move into a clean dotnet ubuntu lean image
FROM mcr.microsoft.com/dotnet/runtime:8.0.10-jammy as app-cont

LABEL name="vnuge/vncache"
LABEL maintainer="Vaughn Nugent <vnpublic@proton.me>"
LABEL description="A simple clustered network data caching service"

#install dumb-init and task
RUN apt update && apt install -y dumb-init 

#copy local artifacts again in run container
COPY app/ /app

#pull compiled libs from build container
COPY --from=native-cont /build/lib/vnlib_rpmalloc.so /app/lib/vnlib_rpmalloc.so
COPY --from=native-cont /build/lib/vnlib_compress.so /app/lib/vnlib_compress.so

#also copy the task deb and install it
COPY --from=native-cont /build/task_linux_amd64.deb /usr/local/bin/task.deb
RUN dpkg -i /usr/local/bin/task.deb

#workdir 
WORKDIR /app

#default to 2557 for cache port
EXPOSE 2557/tcp

#expose an assets directory for custom assets install
VOLUME /app/usr/assets

#path to the inlcuded compression assembly path
ENV COMPRESSOR_ASM_PATH=lib/vnlib.net.compression/VNLib.Net.Compression.dll 

#run permissions setup 
RUN task container-set-perms

#run the init script within dumb-init (trailing -- allows uers to pass args to the entrypoint)
ENTRYPOINT ["dumb-init", "--" ]
CMD ["task", "container-run", "--", "--input-off"]

