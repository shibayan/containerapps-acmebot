<h1 align="center">
  Container Apps Acmebot
</h1>
<p align="center">
  Automated ACME SSL/TLS certificates issuer for Azure Container Apps
</p>
<p align="center">
  <a href="https://github.com/shibayan/containerapps-acmebot/actions/workflows/build.yml" rel="nofollow"><img src="https://github.com/shibayan/containerapps-acmebot/workflows/Build/badge.svg" alt="Build" style="max-width: 100%;"></a>
  <a href="https://github.com/shibayan/containerapps-acmebot/releases/latest" rel="nofollow"><img src="https://badgen.net/github/release/shibayan/containerapps-acmebot" alt="Release" style="max-width: 100%;"></a>
  <a href="https://github.com/shibayan/containerapps-acmebot/stargazers" rel="nofollow"><img src="https://badgen.net/github/stars/shibayan/containerapps-acmebot" alt="Stargazers" style="max-width: 100%;"></a>
  <a href="https://github.com/shibayan/containerapps-acmebot/network/members" rel="nofollow"><img src="https://badgen.net/github/forks/shibayan/containerapps-acmebot" alt="Forks" style="max-width: 100%;"></a>
  <a href="https://github.com/shibayan/containerapps-acmebot/blob/master/LICENSE"><img src="https://badgen.net/github/license/shibayan/containerapps-acmebot" alt="License" style="max-width: 100%;"></a>
  <a href="https://registry.terraform.io/modules/shibayan/containerapps-acmebot/azurerm/latest" rel="nofollow"><img src="https://badgen.net/badge/terraform/registry/5c4ee5" alt="Terraform" style="max-width: 100%;"></a>
  <br>
  <a href="https://github.com/shibayan/containerapps-acmebot/commits/master" rel="nofollow"><img src="https://badgen.net/github/last-commit/shibayan/containerapps-acmebot" alt="Last commit" style="max-width: 100%;"></a>
  <a href="https://github.com/shibayan/containerapps-acmebot/wiki" rel="nofollow"><img src="https://badgen.net/badge/documentation/available/ff7733" alt="Documentation" style="max-width: 100%;"></a>
  <a href="https://github.com/shibayan/containerapps-acmebot/discussions" rel="nofollow"><img src="https://badgen.net/badge/discussions/welcome/ff7733" alt="Discussions" style="max-width: 100%;"></a>
</p>

## Motivation

We have started to address the following requirements:

- Support for multiple Container Apps and Container Apps Environment
- Easy to deploy and configure
- Highly reliable implementation
- Ease of Monitoring (Application Insights, Webhook)

You can add multiple certificates to a single Container Apps.

## Feature Support

- Issuing certificates for Zone Apex / Multi-domain / Wildcard
- Automatic binding of custom domains and certificates to Container App
- Support for multiple Container Apps in a single application
- ACME-compliant Certification Authorities
  - [Let's Encrypt](https://letsencrypt.org/)
  - [Buypass Go SSL](https://www.buypass.com/ssl/resources/acme-free-ssl)
  - [ZeroSSL](https://zerossl.com/features/acme/) (Requires EAB Credentials)

## Deployment

| Azure (Public) | Azure China | Azure Government |
| :---: | :---: | :---: |
| <a href="https://portal.azure.com/#create/Microsoft.Template/uri/https%3A%2F%2Fraw.githubusercontent.com%2Fshibayan%2Fcontainerapps-acmebot%2Fmaster%2Fazuredeploy.json" target="_blank"><img src="https://aka.ms/deploytoazurebutton" /></a> | <a href="https://portal.azure.cn/#create/Microsoft.Template/uri/https%3A%2F%2Fraw.githubusercontent.com%2Fshibayan%2Fcontainerapps-acmebot%2Fmaster%2Fazuredeploy.json" target="_blank"><img src="https://aka.ms/deploytoazurebutton" /></a> | <a href="https://portal.azure.us/#create/Microsoft.Template/uri/https%3A%2F%2Fraw.githubusercontent.com%2Fshibayan%2Fcontainerapps-acmebot%2Fmaster%2Fazuredeploy.json" target="_blank"><img src="https://aka.ms/deploytoazurebutton" /></a> |

Learn more at https://github.com/shibayan/containerapps-acmebot/wiki/Getting-Started

## Thanks

- Based on [containerapps-acmebot](https://github.com/jeffhollan/containerapps-acmebot) by @jeffhollan
- [ACMESharp Core](https://github.com/PKISharp/ACMESharpCore) by @ebekker
- [Durable Functions](https://github.com/Azure/azure-functions-durable-extension) by @cgillum and contributors
- [DnsClient.NET](https://github.com/MichaCo/DnsClient.NET) by @MichaCo

## License

This project is licensed under the [Apache License 2.0](https://github.com/shibayan/containerapps-acmebot/blob/master/LICENSE)
