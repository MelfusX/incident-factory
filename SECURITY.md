# Security policy

incident-factory is a **deliberately vulnerable / breakable** sample. Its unsafe and
failure-prone code paths are the point of the project: they exist so IncidentCompass and
other observability / AI-triage tooling have realistic incidents to detect, triage, and
(later) repair.

## Do not report the intentional flaws

The bugs, unsafe patterns, and failing scenarios in this repository are intentional and
documented. Please do not open security reports for them - they are working as designed.

## Do not deploy this

Run it only locally or in an isolated sandbox. Never deploy it to a shared, internet-facing,
or production-like environment, and do not reuse its code in real systems.

## Genuinely unintended problems

If you find something *unintended* - for example a real leaked secret, or a vulnerability in a
third-party dependency - open a normal issue. There is no private disclosure process for this
sample.
