---
name: travel-planning
description: Plans a trip interactively in the chat using rendered A2UI surfaces — collects destination, dates, travellers and trip style, presents three fabricated itinerary options with prices, and walks the user through review and confirmation. Use when the user wants to plan, book, price or compare a trip, holiday, vacation, flight or hotel stay, or asks for help choosing between travel options. Demonstration only — all itineraries and prices are invented, nothing is booked.
license: Apache-2.0
compatibility: Requires the render_skill_surface tool and a browser client with the A2UI renderer.
metadata:
  author: ntg-agent
  version: "1.0"
---

# Travel planning

You plan a trip as a three-step visual flow. Each step renders a real interactive
surface in the user's chat; the user fills it in and submits, and their answers come
back to you as a `log_a2ui_event` tool call.

**This is a demonstration.** There is no travel API. You invent the itineraries,
airlines, hotels and prices yourself — they must be plausible and internally
consistent, never real bookings. Say so once, in the confirmation step, and never
claim anything was actually reserved.

## The flow

| Step | Surface | You call | User submits action |
|---|---|---|---|
| 1. Collect | `trip-search` | `render_skill_surface` | `trip_search_submit` |
| 2. Offer | `trip-results` | `render_skill_surface` | `trip_option_selected` |
| 3. Review | `trip-confirm` | `render_skill_surface` | `trip_booking_confirmed` or `trip_change_requested` |

Render exactly one surface per turn, then stop and wait. Never render two steps in
the same turn — the user has not answered the first one yet.

## Step 1 — Collect the trip

Call `render_skill_surface` with `skill: "travel-planning"`, `surface: "trip-search"`.

Pre-fill anything the user already told you via `values`; leave the rest as-is:

```json
{
  "skill": "travel-planning",
  "surface": "trip-search",
  "values": {
    "trip": {
      "destination": "Da Nang",
      "departDate": "2026-09-12",
      "returnDate": "2026-09-19",
      "travellers": 2,
      "style": ["balanced"]
    }
  }
}
```

Dates are `YYYY-MM-DD` strings. `travellers` is a number, 1–8. `style` is an **array**
with one of `"budget"`, `"balanced"`, `"comfort"` — the picker stores its selection as
an array even in single-choice mode.

Say one short line before the surface ("Let's set up your trip.") and nothing after it.
Do not restate the fields in prose — the surface already shows them.

## Step 2 — Offer three options

When `trip_search_submit` arrives, read the answers (see "Reading answers" below),
then invent **three** genuinely different options — not three near-identical ones:

- a cheaper, less convenient option (budget airline, longer layover, simpler hotel)
- a middle option that is the obvious default
- a premium option (direct flight, better location, more included)

Prices must reflect the trip length, traveller count and the chosen style, and must
increase across the three. Use the user's own currency if they named one, otherwise USD.

Call `render_skill_surface` with `surface: "trip-results"`:

```json
{
  "skill": "travel-planning",
  "surface": "trip-results",
  "values": {
    "results": {
      "heading": "Three options for Da Nang",
      "subheading": "12–19 Sep · 2 travellers · balanced",
      "o1": { "name": "Budget saver",   "detail": "1 stop · 3-star beachfront · room only",        "price": "$610" },
      "o2": { "name": "Balanced pick",  "detail": "Direct · 4-star riverside · breakfast",         "price": "$840" },
      "o3": { "name": "Comfort plus",   "detail": "Direct · 5-star beachfront · breakfast + spa",  "price": "$1,290" }
    }
  }
}
```

`detail` is one line — keep it under about 45 characters or it wraps awkwardly next
to the price. `price` is a preformatted string including the currency symbol.

## Step 3 — Review and confirm

When `trip_option_selected` arrives, the `choice` value is `"o1"`, `"o2"` or `"o3"`.
Render `trip-confirm` with the full summary, filling `total` from the option the user
picked:

```json
{
  "skill": "travel-planning",
  "surface": "trip-confirm",
  "values": {
    "confirm": {
      "destination": "Da Nang, Vietnam",
      "dates": "12–19 Sep 2026 (7 nights)",
      "travellers": "2 adults",
      "option": "Balanced pick — direct, 4-star riverside",
      "total": "$840"
    }
  }
}
```

Then:

- On `trip_booking_confirmed` — reply in text confirming the plan, restate the option
  name and total, and state plainly that this is a demonstration and no booking was made.
- On `trip_change_requested` — go back to step 1, re-rendering `trip-search` with the
  values the user already gave pre-filled.

## Reading answers

Every action arrives as a `log_a2ui_event` tool call whose context contains the values
the button named **and** a `formData` object holding the complete surface state.

Check in this order:

1. The named value (e.g. `destination`, `choice`).
2. `formData` under the bound path — `formData.trip.destination`, `formData.results.choice`.
3. `formData.__inputs.<componentId>` — the fallback the client always writes, keyed by
   component id (`search-destination`, `results-picker`, …).

Step 3 is not a last resort here, it is often where the truth is: the client mirrors
every input into `__inputs` on each keystroke regardless of binding. Never tell the
user nothing was selected without checking all three.

Picker values are arrays. `style` and `choice` come back as `["balanced"]` and `["o2"]`
— take the first element.

## Gotchas

- **One surface per turn.** Each render creates a new card in the chat; the previous
  ones stay visible as a trail of the conversation. That is intended — do not try to
  update or delete an earlier surface.
- **Never invent a real booking reference, airline PNR, or payment confirmation.**
  Fabricate itineraries and prices; never fabricate anything that looks like proof of
  a transaction.
- **Don't narrate the UI.** Do not describe the buttons or tell the user to click
  something — they can see it. One short line of framing is enough.
- **If the user answers in text instead of using the surface**, just accept it and move
  to the next step. Don't insist they use the form.
- **If the user asks for something outside a trip plan** (visa rules, weather, packing
  advice), answer normally in text. Only render a surface when you are advancing one of
  the three steps above.
