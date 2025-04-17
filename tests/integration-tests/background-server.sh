#!/bin/bash

set -e

ACTION=$1
SERVER_DIR=$2
SERVER_ARGS=$3
PID_FILE="$(dirname "$0")/server.pid"

start_server() {
    echo "Starting test server in $SERVER_DIR with args: $SERVER_ARGS"
    
    # Change to server directory
    cd "$SERVER_DIR" || { echo "Server directory not found"; exit 1; }
    
    # Start the server in background
    nohup task $SERVER_ARGS > server.log 2>&1 &
    
    # Store the PID
    echo $! > "$PID_FILE"
    echo "Server started with PID: $!"
    echo "PID stored in $PID_FILE"
    
    # Return to original directory
    cd - > /dev/null
}

stop_server() {
    if [ -f "$PID_FILE" ]; then
        PID=$(cat "$PID_FILE")
        echo "Stopping server with PID: $PID"
        
        # Kill the process
        kill -15 "$PID" 2>/dev/null || kill -9 "$PID" 2>/dev/null || true
        echo "Server process stopped"
        
        # Remove PID file
        rm "$PID_FILE"
    else
        echo "No PID file found at $PID_FILE. Server may not be running."
    fi
}

# Execute the requested action
case "$ACTION" in
    start)
        start_server
        ;;
    stop)
        stop_server
        ;;
    *)
        echo "Usage: $0 {start|stop} [server_dir] [server_args]"
        exit 1
        ;;
esac