# Security Policy

## Supported versions

| Version | Supported |
| ------- | --------- |
| 0.4.x   | Yes       |
| < 0.4.0 | No        |

Only the latest minor release is supported while RazorKit is below 1.0. Please upgrade to the latest 0.4.x release.

## Deployment

RazorKit is public by default: any environment that maps it serves `/razorkit` and `/razorkit-preview` to anyone who can reach the site. If it should not be reachable in production, set `RazorKitOptions.EnabledEnvironments` to the environments that should serve it. See "Where RazorKit is served" in the README.

## Reporting a vulnerability

Report vulnerabilities privately. Do not open a public issue, pull request or discussion.

Use GitHub's private vulnerability reporting: go to the **Security** tab of this repository and choose **Report a vulnerability**.

Please include:

- the affected package version and target framework (net8.0, net9.0 or net10.0)
- a description of the problem and its impact
- steps to reproduce, or a minimal proof of concept
- any suggested fix or mitigation

## What to expect

We will acknowledge your report, keep you informed while we investigate, and credit you in the advisory if you wish.

Fixes are disclosed through a GitHub security advisory once consumers have had time to upgrade.
