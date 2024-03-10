#Copyright (c) Vaughn Nugent
#Licensed under the GNU AGPL V3.0

#use plain alpine latest to build native libraries in
FROM alpine:3.19 as native-cont

#install public libs and build tools
RUN apk update && apk add build-base cmake npm
#most universal way to use Task is from NPM
RUN npm install -g @go-task/cli

WORKDIR /build
 
#include local artifacts
COPY app/ .

#build internal libraries and copy the libraries to the /lib output directory
RUN mkdir out/
RUN task build-libs

#APP CONTAINER
#move into a clean dotnet apline lean image
FROM mcr.microsoft.com/dotnet/runtime:8.0.2-alpine3.19-amd64 as app-cont

LABEL name="vnuge/vncache"
LABEL maintainer="Vaughn Nugent <vnpublic@proton.me>"
LABEL description="A simple clustered network data caching service"

#copy local artifacts again in run container
COPY app/ /app

#pull compiled libs from build container
COPY --from=native-cont /build/out /app/lib

RUN apk update && apk add --no-cache gettext icu-libs dumb-init

#workdir 
WORKDIR /app

#default to 2557 for cache port
EXPOSE 2557/tcp

VOLUME /app/ssl
#expose an assets directory for custom assets install
VOLUME /app/usr/assets

#disable dotnet invariant culture on alpine
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=0

#add helper/required libraries
#ENV VNLIB_SHARED_HEAP_FILE_PATH=/app/lib/libvn_rpmalloc.so		not ready yet, still need to debug

#cache varables
ENV MAX_ENTRIES=10000
ENV CACHE_BUCKETS=100
ENV CACHE_MAX_MESSAGE=20480
ENV MAX_CONCURRENT_CONNECTIONS=1000

ENV VERIFY_IP=true
ENV MAX_PEER_NODES=10
ENV DISCOVERY_INTERVAL=360
ENV CACHE_CONNECT_PATH="/cache"
ENV DISCOVER_PATH="/discover"
ENV KNOWN_PEERS=[]

#HC Vault
ENV HC_VAULT_ADDR=""
ENV HC_VAULT_TOKEN=""
ENV HC_VAULT_TRUST_CERT=false

#SECRETS
ENV CACHE_PRIV_KEY=""
ENV CLIENT_PUB_KEY=""

#HTTP/PROXY Config
ENV HTTP_DOWNSTREAM_SERVERS=[]
ENV HTTP_MAX_CONNS=5000

#run the init script within dumb-init
ENTRYPOINT ["dumb-init", "--"]
CMD ["ash", "./run.sh"]

