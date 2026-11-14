// SCUT routing v3: managed by the Windows network assistant.
function main(config, profileName) {
  const campusName = "SCUT-EasyConnect";
  const dockerName = "SCUT-EasyConnect-Docker";
  config.proxies = (config.proxies || []).filter(p => p.name !== campusName && p.name !== dockerName);
  config.proxies.push({name: dockerName, type: "socks5", server: "127.0.0.1", port: 1080, udp: false});
  config["proxy-groups"] = (config["proxy-groups"] || []).filter(p => p.name !== campusName);
  config["proxy-groups"].push({name: campusName, type: "select", proxies: ["DIRECT", dockerName]});
  const campusRules = [
  "DOMAIN,sslvpn3.scut.edu.cn,DIRECT",
  "IP-CIDR,183.62.57.37/32,DIRECT,no-resolve",
  "IP-CIDR,10.48.0.0/16,SCUT-EasyConnect,no-resolve",
  "IP-CIDR,202.38.251.178/32,SCUT-EasyConnect,no-resolve",
  "DOMAIN-SUFFIX,scut.edu.cn,SCUT-EasyConnect"
];
  config.rules = campusRules.concat((config.rules || []).filter(r => !campusRules.includes(r)));
  return config;
}
