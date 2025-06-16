module.exports = {
    apps: [{
        name: "vncache",
        instances: 1,
        interpreter: "dotnet",
        script: "webserver/vnlib.webserver.dll",
        args: "webserver/VNLb.Webserver.dll --config config/config.json --threads 2 --verbose --log-transport --log-http --input-off",
        autorestart: false,
        vizion: false,
        combine_logs: true,
        log_file: "server.log",
        env_linux: {
            VNLIB_SHARED_HEAP_FILE_PATH: "lib/vnlib_rpmalloc.so",
            VNLIB_COMPRESS_DLL_PATH: "lib/vnlib_compress.so"
        },
        env_windows: {
            VNLIB_SHARED_HEAP_FILE_PATH: "lib/vnlib_rpmalloc.dll",
            VNLIB_COMPRESS_DLL_PATH: "lib/vnlib_compress.dll"
        },
        env_darwin: {
            VNLIB_SHARED_HEAP_FILE_PATH: "lib/vnlib_rpmalloc.dylib",
            VNLIB_COMPRESS_DLL_PATH: "lib/vnlib_compress.dylib"
        }
  }]
}
