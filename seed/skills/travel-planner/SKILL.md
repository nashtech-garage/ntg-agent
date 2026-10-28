---
name: travel-planner
description: Plans a trip interactively in one A2UI surface with three tabs — collects destination, dates, traveller counts and trip style, presents five fabricated hotel options with ratings, amenities and USD prices, then reviews the choice. Use when the user wants to plan or compare a trip or hotel stay. Demonstration only — all hotels, addresses and prices are invented, nothing is booked.
license: Apache-2.0
compatibility: Requires the render_skill_surface tool and a browser client with the A2UI renderer.
metadata:
  author: ntg-agent
  version: "2.1"
---

# Travel planner

You plan a trip in **one** surface with three tabs. You render it once at the start, and
after that you render the *same* surface again to fill in the next tab and move the user to
it. The card in the chat updates in place — it does not stack up, and there is never more
than one trip planner on screen.

**This is a demonstration.** There is no travel API. You invent plausible hotel names,
destination-local addresses and prices; they are not verified properties or real bookings. Say
so once, in the confirmation step, and never claim anything was actually reserved.

## The surface

Always `skill: "travel-planner"`, `surface: "trip-planner"`. There is no other surface.

| Tab | Index | Holds | User submits |
|---|---|---|---|
| 1. Trip | `0` | destination, dates, adults, kids, style · **Find trips** | `trip_search_submit` |
| 2. Options | `1` | five hotels with details, benefits and prices, a picker · **Continue to review** | `trip_option_selected` |
| 3. Review | `2` | the summary and total · **Confirm plan** / **Change details** | `trip_booking_confirmed` / `trip_change_requested` |

All three tabs exist from the first render. The user can click ahead at any time; tabs 2 and
3 simply show placeholders until you have filled them.

You move the user by sending the tab index in `__tabs.wizard`. The tab moves only when that
index *changes* — sending the same index twice does nothing, which is deliberate: if the user
has clicked back to an earlier tab to re-read it, a repeat render will not yank them forward.

## The data model

The surface's data is four independent branches, and each render writes them one at a time:

| Branch | Holds | Written by |
|---|---|---|
| `__tabs` | `{ "wizard": 0\|1\|2 }` — which tab is showing | you, on every render |
| `trip` | tab 1's fields: `destination`, `departDate`, `returnDate`, `adults`, `kids`, `style` | you (pre-fill), then the user |
| `options` | tab 2's `heading`, `subheading`, `o1`–`o5` and the user's `choice` | you |
| `review` | tab 3's `destination`, `dates`, `travellers`, `option`, `total` | you |

**Send only the branch you are filling in, plus `__tabs`.** A branch you send is replaced by
what you sent merged over the template's defaults, so re-sending a branch you have nothing new
to say about overwrites live answers with blanks.

One exception, and only one: when you fill `review`, send `options` again with exactly the
values you sent last time. Tab 2 stays on screen behind tab 3, and the user can click back to
it while you are talking.

Nesting matters. `{"trip": "Da Nang"}` is rejected — `trip` is an object, and a value that
changes a branch's shape (object to text, array to text) is refused rather than rendered.

## Step 1 — Render the planner

Render it as soon as the user wants to plan a trip, with tab 0 showing.

**Always carry over what they have already said.** The message that started this almost always
names a destination — "a trip to Da Lat" means you send `trip.destination: "Da Lat"`. Do the
same for any dates, traveller count or style they mentioned. Rendering an empty form after
someone has just told you where they want to go makes them type it twice, and is the single
most common way this skill is used badly. Leave a field out only when they genuinely have not
said anything about it.

```json
{
  "skill": "travel-planner",
  "surface": "trip-planner",
  "values": {
    "__tabs": { "wizard": 0 },
    "trip": {
      "destination": "Da Nang",
      "departDate": "2026-09-12",
      "returnDate": "2026-09-19",
      "adults": 2,
      "kids": 0,
      "style": ["balanced"]
    }
  }
}
```

Dates are `YYYY-MM-DD` strings and must both be future dates. `returnDate` must be greater than
or equal to `departDate`. `destination` is required. `adults` and `kids` are non-negative
whole numbers, with `adults` at least 1. `style` is an **array** with one of `"budget"`,
`"balanced"`, `"comfort"` — the picker stores its selection as an array even in single-choice
mode.

The submit button declares these rules in the surface asset's generic A2UI `checks` metadata.
Do not rely on a skill-specific frontend validator or assume that the client knows the `trip`
data paths; uploaded skills may define different data models and validation rules.

Say one short line before the surface ("Let's set up your trip.") and nothing after it. Do
not restate the fields in prose — the surface already shows them.

## Step 2 — Fill the Options tab

When `trip_search_submit` arrives, read the answers (see "Reading answers" below), then invent
**exactly five distinct hotels** located in or near `trip.destination`. These are fabricated
recommendations, not verified businesses. Use plausible, distinctive property names — never
labels such as "Hotel A", "Hotel 1" or "Option 1" — and make the five properties meaningfully
different in location, character and amenities.

Populate every hotel object `options.o1` through `options.o5` with:

- `name`: a realistic-sounding, destination-appropriate hotel name.
- `description`: one concise description of about 15 words.
- `hotelStars`: a fictional hotel classification from 1 through 6 stars, suitable for the trip
  style. Format it as exactly that many filled stars followed by the matching label, for example
  `★★★ 3-star`. The star count and number in the label must match.
- `rating`: a varied, random-looking decimal score formatted as `9.5/10`, with a range
  reflecting the selected style: budget `7.0`–`8.8`, balanced `8.1`–`9.4`, comfort
  `8.8`–`9.9`. Provide only the value; the surface adds the separate user-rating star icon, so
  do not include a star or a "Rating:" label.
- `address`: a plausible but invented street address in or near the requested destination.
  Do not imply that it has been verified.
- `price`: a USD string for the whole stay and party, formatted with `$` followed only by
  digits and optional thousands separators, such as `$90` or `$1,240`. Never spell out a number
  (`$ninety`), write "USD" in place of digits, or use decimal text. Calculate it from the number
  of nights, adults and kids (for example, nightly adult rate × nights ×
  `(adults + 0.5 × kids)`). Keep rates consistent with the selected style: budget properties
  are least expensive, balanced properties mid-range, and comfort properties highest. Order
  the five choices from lowest to highest total price. Before rendering, verify that all five
  prices match `^\$\d{1,3}(,\d{3})*$`.
- `benefit1`, `benefit2`, `benefit3`: three style-appropriate amenities, each with `icon` and
  `name`. `icon` must be a visible Unicode emoji pictogram such as `📶`, `☕`, `🏊`, `🧖`,
  `♨️`, `🏖️` or `🅿️` — never an icon identifier such as `info` or `shoppingCart`.

Benefits should fit the chosen trip style: budget stays might highlight free Wi-Fi and breakfast;
balanced stays can add a pool or family amenities; comfort stays can feature a spa, sauna or
private beach. Do not use the word "illustrative" in any displayed heading, description or
address, and do not append parenthetical notes such as "(illustrative address)". Use natural
copy, and note once in the subheading that the estimates are fictional and unverified. Hotel star
classifications, ratings, descriptions, addresses, benefits and prices must all be present for
all five objects. Do not send `choice`; that belongs to the user.

The user is already looking at this tab. Clicking **Find trips** moves them straight to
"2. Options", where they are watching placeholder text while you work — so answer promptly.
Render `trip-planner` with `__tabs.wizard: 1`, the submitted `trip` branch carried forward
unchanged, and the complete `options` branch. Carrying `trip` forward is required: each render
replays the surface from its persisted operations, so omitting it would restore the first tab's
template defaults and lose the user's answers on tab changes or conversation reloads.

## Step 3 — Fill the Review tab

When `trip_option_selected` arrives, `choice` is a one-element array containing `o1`, `o2`,
`o3`, `o4` or `o5`. Read the selected hotel's name, `hotelStars`, price and benefit names from
`formData.options`, then render `trip-planner` with tab 2, the submitted `trip` branch unchanged,
all five hotel objects and the user's `choice` carried forward unchanged, and a `review` branch.
The review contains
five plain strings you write for the user to read — destination, dates, travellers, selected
hotel summary and the selected hotel's total USD price — not the raw field values.

Build `review.option` from the selected hotel's name, hotel star class, a concise location or
character descriptor from its description when useful, and all three benefit names from
`formData.options`. Convert the selected `hotelStars` value to its matching `N-star` label, then
format the summary like `Siam Garden Hotel — 3-star, central, breakfast included, free Wi-Fi,
family rooms`. Preserve the benefit names and do not include their emoji icons in this summary.

For `review.destination`, display the requested place with its country when the country is not
already included. For example, show `Ho Chi Minh City, Viet Nam` when the submitted destination
is only `Ho Chi Minh City`. Do not repeat a country that is already present, and leave a
destination that is already a country as-is. Preserve the user's place name and spelling; if its
country is genuinely ambiguous, do not guess.

Then:

- On `trip_booking_confirmed` — reply in text confirming the plan, restate the option name and
  total, and state plainly that this is a demonstration and no booking was made. Do not render
  again.
- On `trip_change_requested` — render `trip-planner` with `__tabs.wizard: 0` and the `trip`
  branch re-filled with everything the user already gave you, so they are back on tab 1 with
  their answers in front of them. Then run steps 2 and 3 again from the top.

## Reading answers

Every action arrives as a `log_a2ui_event` tool call whose context contains the values the
button named **and** a `formData` object holding the complete surface state.

Check in this order:

1. The named value (e.g. `destination`, `choice`).
2. `formData` under the bound path — `formData.trip.destination`, `formData.options.choice`.
3. `formData.__inputs.<componentId>` — the fallback the client always writes, keyed by
   component id: `trip-destination`, `trip-depart`, `trip-return`, `trip-adults`, `trip-kids`,
   `trip-style`, `options-picker`.

Step 3 is not a last resort here, it is often where the truth is: the client mirrors every
input into `__inputs` on each keystroke regardless of binding, and — unlike the four branches
above — nothing you render ever overwrites it. It is the one place an answer from an earlier
tab is guaranteed to still be sitting on a later turn.

`formData.__inputs.wizard` holds where the user is: `{ "index": 1, "title": "2. Options" }`.
Use it to tell a submission from the tab the user has since wandered to, and never to argue
with them about which tab they are on.

Picker values are arrays. `style` and `choice` come back as `["balanced"]` and `["o1"]` through
`["o5"]` — take the first element.

## Gotchas

- **One render per reply.** Fill one tab, move the user to it, then stop and wait. Never
  render twice in a turn to "get ahead" — the user has not answered the first one yet.
- **Never a second surface.** `trip-planner` is the whole flow. Re-rendering it updates the
  card that is already there; you are editing the planner, not posting a new one.
- **Never invent a real booking reference or payment confirmation.** Fabricate hotel suggestions
  and prices; never fabricate anything that looks like proof of a transaction.
- **Don't narrate the UI.** Do not describe the tabs or buttons or tell the user to click
  something — they can see it. One short line of framing is enough.
- **If the user answers in text instead of using the surface**, just accept it and fill the
  next tab from what they said. Don't insist they use the form.
- **If the user asks for something outside a trip plan** (visa rules, weather, packing advice),
  answer normally in text. Only render when you are advancing one of the three tabs.
