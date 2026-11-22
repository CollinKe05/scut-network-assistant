// SCUT routing v3: managed by the Windows network assistant.
function main(config, profileName) {
  const campusName = "Campus-Network";
  const dockerName = "Campus-Docker";
  config.proxies = (config.proxies || []).filter(p => p.name !== campusName && p.name !== dockerName);
  config.proxies.push({name: dockerName, type: "socks5", server: "127.0.0.1", port: 1080, udp: false});
  config["proxy-groups"] = (config["proxy-groups"] || []).filter(p => p.name !== campusName);
  config["proxy-groups"].push({name: campusName, type: "select", proxies: ["DIRECT", dockerName]});
  const campusRules = [
  "DOMAIN,sslvpn3.scut.edu.cn,DIRECT",
  "DOMAIN-SUFFIX,scut.edu.cn,Campus-Network"
];
  config.rules = campusRules.concat((config.rules || []).filter(r => !campusRules.includes(r)));
  return config;
}
