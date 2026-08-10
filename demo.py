"""Run four scenarios and print the JSON the orchestrator returns.

Run from the repository root:

    .venv/bin/python demo.py
"""

from __future__ import annotations

import json

from orchestrator import read_audit_log, run_workflow, vendor_status

QUESTION = "Can we approve Vendor X to process customer payment data?"

SCENARIOS = [
    (
        "1. tenant-a asks, no action requested",
        {
            "tenantId": "tenant-a",
            "userId": "analyst@tenant-a.example",
            "role": "analyst",
            "question": QUESTION,
        },
    ),
    (
        "2. tenant-b requests markVendorApproved with no approval",
        {
            "tenantId": "tenant-b",
            "userId": "approver@tenant-b.example",
            "role": "approver",
            "question": QUESTION,
            "requestedAction": "markVendorApproved",
        },
    ),
    (
        "3. tenant-b requests markVendorApproved with a registered approver",
        {
            "tenantId": "tenant-b",
            "userId": "approver@tenant-b.example",
            "role": "approver",
            "question": QUESTION,
            "requestedAction": "markVendorApproved",
            "approvedBy": "compliance@tenant-b.example",
        },
    ),
    (
        "4. tenant-b, viewer role, valid approval — authorization still refuses",
        {
            "tenantId": "tenant-b",
            "userId": "viewer@tenant-b.example",
            "role": "viewer",
            "question": QUESTION,
            "requestedAction": "markVendorApproved",
            "approvedBy": "compliance@tenant-b.example",
        },
    ),
]


def main() -> None:
    for title, request in SCENARIOS:
        print(f"\n=== {title} ===")
        print("request:", json.dumps(request, indent=2))
        print("response:", json.dumps(run_workflow(request), indent=2))

    print("\n=== vendor state after the run ===")
    for tenant in ("tenant-a", "tenant-b"):
        print(f"  {tenant}: vendor-x is {vendor_status(tenant, 'vendor-x')}")

    print("\n=== audit log ===")
    for event in read_audit_log():
        print(f"  {event.event_id}  {event.event_type.value:<15} {event.tenant_id}  {event.user_id}")


if __name__ == "__main__":
    main()
