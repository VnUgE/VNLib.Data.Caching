#Copyright (c) Vaughn Nugent
#Licensed under the GNU AGPL V3.0

#use plain ubuntu latest to build native libraries in
FROM ubuntu:25.04 AS native-cont

WORKDIR /build

#download and install task (its easier and faster without npm)
ADD --checksum=sha256:cdd55b9908d3ef0889bb2270132f7bdb90e50d85b645c57434385cb8ea80cc42 \
  https://github.com/go-task/task/releases/download/v3.44.0/task_linux_amd64.deb \
  task_linux_amd64.deb

RUN dpkg -i task_linux_amd64.deb

#include local artifacts
COPY app/ .

#installs required dependencies and builds the native libraries
RUN task setup-apt

#APP CONTAINER
#move into a clean dotnet ubuntu lean image
FROM mcr.microsoft.com/dotnet/runtime:8.0.16-noble AS app-cont

LABEL name="vnuge/vncache"
LABEL maintainer="Vaughn Nugent <vnpublic@proton.me>"
LABEL description="A simple clustered network data caching service"

#copy local artifacts again in run container
COPY app/ /app

#pull compiled libs from build container
COPY --from=native-cont /build/lib/vnlib_rpmalloc.so /app/lib/vnlib_rpmalloc.so
COPY --from=native-cont /build/lib/vnlib_compress.so /app/lib/vnlib_compress.so

#also copy the task deb from native build container and install it
COPY --from=native-cont /build/task_linux_amd64.deb /usr/local/bin/task.deb
RUN dpkg -i /usr/local/bin/task.deb && rm /usr/local/bin/task.deb

#add dumb-init deb from build container
ADD --checksum=sha256:9e842e2480e1dee9f0cd482701ba89ee85b3d9c2ca249d38b6c0f49733b12f16  \
  https://github.com/Yelp/dumb-init/releases/download/v1.2.5/dumb-init_1.2.5_amd64.deb  \
  /usr/local/bin/dumb-init.deb
RUN dpkg -i /usr/local/bin/dumb-init.deb && rm /usr/local/bin/dumb-init.deb

WORKDIR /app

#default to 2557 for cache port
EXPOSE 2557/tcp
ENV HTTP_BIND_PORT=2557

#expose an assets directory for custom assets install
VOLUME /app/usr/assets

#path to the inlcuded compression assembly path
ENV COMPRESSOR_ASM_PATH=lib/vnlib.net.compression/VNLib.Net.Compression.dll

#run permissions setup 
RUN task container-set-perms

#run the init script within dumb-init (trailing -- allows uers to pass args to the entrypoint)
ENTRYPOINT ["dumb-init", "--" ]
CMD ["task", "container-run", "--", "--input-off"]

