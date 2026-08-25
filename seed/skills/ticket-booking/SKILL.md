---
name: ticket-booking
description: Books tickets to an event interactively in the chat using rendered A2UI surfaces — collects the event, city, date and party size, offers three fabricated seating tiers as tabs with seats and prices, and walks the user through review and confirmation. Use when the user wants to find, price, compare or book tickets or seats for a concert, gig, show, play, festival or match. Demonstration only — every event, seat and price is invented, nothing is booked.
license: Apache-2.0
compatibility: Requires the render_skill_surface tool and a browser client with the A2UI renderer.
metadata:
  author: ntg-agent
  version: "1.0"
---

# Ticket booking

You book tickets to an event as a three-step visual flow. Each step renders a real
interactive surface in the user's chat; the user fills it in and submits, and their answers
come back to you as a `log_a2ui_event` tool call.

**This is a demonstration.** There is no ticketing API and no seat inventory. You invent
the events, venues, blocks, rows and prices yourself — they must be plausible and
internally consistent, never real seats. Say so once, in the confirmation step, and never
claim a seat was held, issued or paid for.

## The flow

| Step | Surface | You call | User submits action |
|---|---|---|---|
| 1. Collect | `event-search` | `render_skill_surface` | `ticket_search_submit` |
| 2. Offer | `ticket-options` | `render_skill_surface` | `ticket_tier_selected` |
| 3. Review | `booking-review` | `render_skill_surface` | `ticket_booking_confirmed` or `ticket_change_requested` |

Render exactly one surface per turn, then stop and wait. Never render two steps in the same
turn — the user has not answered the first one yet.

## Step 1 — Collect the event

Call `render_skill_surface` with `skill: "ticket-booking"`, `surface: "event-search"`.

Pre-fill anything the user already told you via `values`; leave the rest as-is:

```json
{
  "skill": "ticket-booking",
  "surface": "event-search",
  "values": {
    "search": {
      "event": "Coldplay",
      "city": "Ho Chi Minh City",
      "date": "2026-11-14",
      "tickets": 2,
      "priority": ["view"],
      "together": true
    }
  }
}
```

`date` is a `YYYY-MM-DD` string. `tickets` is a number, 1–6. `priority` is an **array** with
one of `"price"`, `"view"`, `"experience"` — the picker stores its selection as an array
even in single-choice mode. `together` is a boolean.

Say one short line before the surface ("Let's find you seats.") and nothing after it. Do not
restate the fields in prose — the surface already shows them.

## Step 2 — Offer three seating tiers

When `ticket_search_submit` arrives, read the answers (see "Reading answers" below), then
invent **three** genuinely different tiers — not three near-identical ones:

- a cheap tier that costs the user something real (standing, restricted view, far back)
- a middle tier that is the obvious default
- a premium tier (front block, early entry, more included)

`price` is per ticket and must reflect the event, the city and the priority the user chose;
it must increase across the three. `total` is that price times the ticket count. Use the
user's own currency if they named one, otherwise USD. If they asked for seats together,
give consecutive seat numbers in one row.

Call `render_skill_surface` with `surface: "ticket-options"`:

```json
{
  "skill": "ticket-booking",
  "surface": "ticket-options",
  "values": {
    "tiers": {
      "heading": "Three ways into Coldplay",
      "subheading": "Sat 14 Nov · Thong Nhat Stadium · 2 tickets",
      "t1": {
        "name": "Standing",
        "seats": "General admission, pitch rear",
        "price": "$62 each",
        "total": "$124 total",
        "p1": "Unreserved — first in picks the spot",
        "p2": "No seat, and no re-entry once inside"
      },
      "t2": {
        "name": "Lower tier",
        "seats": "Block C, row 14, seats 7–8",
        "price": "$118 each",
        "total": "$236 total",
        "p1": "Side-on view, level with the stage",
        "p2": "Seats together, on the aisle"
      },
      "t3": {
        "name": "Front block",
        "seats": "Block A, row 4, seats 11–12",
        "price": "$245 each",
        "total": "$490 total",
        "p1": "Centre, about 20 m from the stage",
        "p2": "Early entry and a drink included"
      }
    }
  }
}
```

Each tier is a tab, so `name` is the **tab title** — one or two words, or the tabs wrap.
`seats` is one line naming a block and row. `price` and `total` are preformatted strings
including the currency symbol. `p1` and `p2` are the two lines under the seats: keep them
under about 45 characters, and make one of them the honest downside for the cheaper tiers.

## Step 3 — Review and confirm

When `ticket_tier_selected` arrives, `choice` is an array holding one of `"t1"`, `"t2"` or
`"t3"` — read `choice[0]`, not `choice`. Render `booking-review` with the full summary,
filling `tier`, `seats` and `total` from the tier the user picked:

```json
{
  "skill": "ticket-booking",
  "surface": "booking-review",
  "values": {
    "review": {
      "event": "Coldplay — Music of the Spheres",
      "venue": "Thong Nhat Stadium, Ho Chi Minh City",
      "when": "Sat 14 Nov 2026, doors 18:00",
      "tickets": "2 tickets, seated together",
      "tier": "Lower tier",
      "seats": "Block C, row 14, seats 7–8",
      "total": "$236"
    }
  }
}
```

Leave `acknowledged` alone — the tick box is the user's, not yours to pre-set.

Then:

- On `ticket_booking_confirmed` — reply in text restating the tier, the seats and the total,
  and state plainly that this is a demonstration and nothing was reserved. If `acknowledged`
  came back `false`, lead with that sentence instead of closing with it.
- On `ticket_change_requested` — go back to step 1, re-rendering `event-search` with the
  values the user already gave pre-filled.

## Reading answers

Every action arrives as a `log_a2ui_event` tool call whose context contains the values the
button named **and** a `formData` object holding the complete surface state.

Check in this order:

1. The named value (e.g. `event`, `choice`).
2. `formData` under the bound path — `formData.search.event`, `formData.tiers.choice`.
3. `formData.__inputs.<componentId>` — the fallback the client always writes, keyed by
   component id (`event`, `priority`, `together`, `picker`, `ack`, …).

Step 3 is not a last resort here, it is often where the truth is: the client mirrors every
input into `__inputs` on each keystroke regardless of binding. Never tell the user nothing
was selected without checking all three.

Picker values are arrays. `priority` and `choice` come back as `["view"]` and `["t2"]` —
take the first element. Check boxes come back as booleans, and an untouched box holds the
value the surface was seeded with, not `null`.

## Gotchas

- **One surface per turn.** Each render creates a new card in the chat; the previous ones
  stay visible as a trail of the conversation. That is intended — do not try to update or
  delete an earlier surface.
- **The tabs are not the choice.** A user can read all three tiers without picking one; only
  the picker records a selection. If `choice` comes back empty, ask which tier they want —
  do not assume the tab they left open.
- **Never invent a booking reference, ticket number, barcode, order id or payment
  confirmation.** Fabricate events, seats and prices; never fabricate anything that looks
  like proof of a transaction.
- **Don't narrate the UI.** Do not describe the tabs or tell the user to click something —
  they can see it. One short line of framing is enough.
- **If the user answers in text instead of using the surface**, just accept it and move to
  the next step. Don't insist they use the form.
- **If the user asks for something outside a ticket purchase** (age limits, getting to the
  venue, what the support act is), answer normally in text. Only render a surface when you
  are advancing one of the three steps above.
