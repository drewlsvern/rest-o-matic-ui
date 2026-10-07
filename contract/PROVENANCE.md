# Where this contract comes from

`checkin/v1/` is a copy of `contract/checkin/v1/` in
[rest-o-matic](https://github.com/drewlsvern/rest-o-matic), which owns it. If
the two differ, the original wins. Do not edit the copy: change the contract
in rest-o-matic, then run `scripts/sync-contract.sh` here.

| | |
|---|---|
| Last changed in | `82c75ef` (2026-10-04): feat(central): enrol a host with the central app and check in on every tick (#28) |
| First release with it | v0.2.0-rc.1 |
| Copied | 2026-10-05 |

Keeping the copy current is manual for now. Within v1 the contract only adds
fields, which both sides ignore, so a stale copy breaks nothing; new fields
just go untested here. An incompatible change arrives as a `v2` folder.
