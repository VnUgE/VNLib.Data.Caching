#! /bin/sh

#this script will be invoked by dumb-init in the container on statup and is located at /app

rm -rf config && mkdir config

#substitude all -template files in the config-templates/ dir and write them to the config/ dir
echo "Compiling server configuration"
for file in config-templates/*-template.json; do
	envsubst < $file > config/$(basename $file -template.json).json
done

#merge user assets with our assets dir
echo "Merging your asset files"
cp usr/assets/* plugins/assets/ -rf

echo "Starting VNCache server"

#start the server
dotnet webserver/VNLib.WebServer.dll --config config/config.json $SERVER_ARGS