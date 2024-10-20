#Copyright (c) Vaughn Nugent
#Licensed under the GNU AGPL V3.0

#use plain ubuntu latest to build native libraries in
FROM ubuntu:jammy as native-cont

#install public libs and build tools
RUN apt update && apt install -y build-essentials cmake npm
#most universal way to use Task is from NPM
RUN npm install -g @go-task/cli

WORKDIR /build
 
#include local artifacts
COPY app/ .

#build internal libraries and copy the libraries to the /lib output directory
RUN mkdir out/
RUN task build-libs

#APP CONTAINER
#move into a clean dotnet ubuntu lean image
FROM mcr.microsoft.com/dotnet/runtime:8.0.10-jammy-chiseled as app-cont

LABEL name="vnuge/vncache"
LABEL maintainer="Vaughn Nugent <vnpublic@proton.me>"
LABEL description="A simple clustered network data caching service"

#copy local artifacts again in run container
COPY app/ /app

#pull compiled libs from build container
COPY --from=native-cont /build/out /app/lib

RUN apt update && apt install -y gettext dumb-init

#workdir 
WORKDIR /app

#default to 2557 for cache port
EXPOSE 2557/tcp

VOLUME /app/ssl
#expose an assets directory for custom assets install
VOLUME /app/usr/assets

#disable dotnet invariant culture on alpine
#ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=0

#add helper/required libraries
ENV VNLIB_SHARED_HEAP_FILE_PATH=/app/lib/libvn_rpmalloc.so

#path to the inlcuded compression assembly path
ENV COMPESSOR_ASM_PATH=lib/vnlib.net.compression/VNLib.Net.Compression.dll \
    COMPRESSOR_LIB_PATH=lib/vnlib_compress

#run the init script within dumb-init
ENTRYPOINT ["dumb-init", "--"]
CMD ["bash", "./run.sh"]

