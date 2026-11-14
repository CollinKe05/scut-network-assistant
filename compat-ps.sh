#!/bin/sh
# Native EasyConnect probes process paths with "ps -ef | grep ...".
case "${1:-}" in
    -ef|-e|aux|-aux) shift ;;
esac
exec /bin/busybox ps "$@"
