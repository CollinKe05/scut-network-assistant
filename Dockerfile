ARG BASE_IMAGE=hagb/docker-easyconnect@sha256:a279ff5e2c623a6ebd31b4a891a2625140ff46c539ec655199fc1cff55d44c12
FROM ${BASE_IMAGE}

# EasyConnect's helper scripts use these legacy command names.
# Reuse the already bundled BusyBox instead of adding host software.
COPY compat-ps.sh /usr/local/bin/ps
RUN chmod 755 /usr/local/bin/ps \
    && ln -sf /bin/busybox /usr/local/bin/route \
    && sed -i 's#\$VPN_SOURCES#${VPN_RESOURCES}/#g' /usr/local/bin/vpn-config.sh
