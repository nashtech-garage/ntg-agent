"use client";

import { useRenderTool } from "@copilotkit/react-core/v2";
import { z } from "zod";

// The browser renders the result of the server-side `get_weather` tool (an MCP tool that runs
// in the .NET backend). The orchestrator streams that tool's call + result over AG-UI, so we
// only need to RENDER it here — there is no frontend handler and no second model call.
const parameters = z.object({
  location: z.string().describe("The city or location the weather was requested for"),
});

// Shape of the `data` object inside the get_weather tool result JSON.
interface WeatherData {
  city?: string;
  country?: string;
  localTime?: string;
  temperatureC?: number;
  condition?: string;
  iconUrl?: string;
  humidity?: number;
  windKph?: number;
  feelsLikeC?: number;
  uv?: number;
  isDay?: boolean;
}

// get_weather returns { success, data, message } (or { success:false, error, message }).
interface WeatherResult {
  success?: boolean;
  data?: WeatherData;
  error?: string;
  message?: string;
}

function parseResult(result: string): WeatherResult | null {
  try {
    let parsed: unknown = JSON.parse(result);
    // The backend may forward the tool result as a JSON-encoded string; unwrap once more.
    if (typeof parsed === "string") {
      parsed = JSON.parse(parsed);
    }
    return parsed as WeatherResult;
  } catch {
    return null;
  }
}

// Map a free-text condition to an emoji. Used only as a fallback when the API gives no icon.
function conditionEmoji(condition?: string): string {
  const c = (condition ?? "").toLowerCase();
  if (/thunder|storm|lightning/.test(c)) return "⛈️";
  if (/snow|sleet|flurr|ice/.test(c)) return "❄️";
  if (/rain|drizzle|shower/.test(c)) return "🌧️";
  if (/fog|mist|haze/.test(c)) return "🌫️";
  if (/partly|few clouds|partly cloudy/.test(c)) return "⛅";
  if (/cloud|overcast/.test(c)) return "☁️";
  if (/clear|sun|fair/.test(c)) return "☀️";
  return "🌡️";
}

function Pill({ children }: { children: React.ReactNode }) {
  return (
    <div className="my-2 inline-flex items-center gap-2 rounded-lg border border-gray-300 dark:border-gray-700 bg-gray-100 dark:bg-gray-800 px-3 py-1.5 text-sm text-gray-700 dark:text-gray-200">
      {children}
    </div>
  );
}

function Metric({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex flex-col">
      <span className="text-xs uppercase tracking-wide text-white/70">{label}</span>
      <span className="text-sm font-medium">{value}</span>
    </div>
  );
}

function WeatherCard({ data }: { data: WeatherData }) {
  const { city, country, localTime, temperatureC, condition, iconUrl, humidity, windKph, feelsLikeC, uv, isDay } = data;
  const place = [city, country].filter(Boolean).join(", ") || "Unknown location";
  // Day/night theming via is_day. Default to the day palette when unknown.
  const gradient = isDay === false ? "from-slate-700 to-slate-900" : "from-sky-500 to-blue-700";

  return (
    <div
      className={`my-2 w-full max-w-sm overflow-hidden rounded-2xl border border-blue-400/30 bg-gradient-to-br ${gradient} text-white shadow-md`}
    >
      <div className="flex items-start justify-between gap-3 px-4 pt-4">
        <div className="min-w-0">
          <div className="truncate text-base font-semibold">{place}</div>
          <div className="text-sm capitalize text-white/80">{condition ?? "—"}</div>
          {localTime && <div className="mt-0.5 text-xs text-white/60">As of {localTime}</div>}
        </div>
        {iconUrl ? (
          // eslint-disable-next-line @next/next/no-img-element
          <img src={iconUrl} alt={condition ?? "weather"} width={56} height={56} className="h-14 w-14 shrink-0" />
        ) : (
          <div aria-hidden className="text-4xl leading-none">
            {conditionEmoji(condition)}
          </div>
        )}
      </div>

      <div className="px-4 pb-1 pt-2">
        <div className="text-5xl font-bold leading-none">
          {typeof temperatureC === "number" ? Math.round(temperatureC) : "—"}
          <span className="align-top text-2xl">°C</span>
        </div>
        {typeof feelsLikeC === "number" && (
          <div className="mt-1 text-xs text-white/70">Feels like {Math.round(feelsLikeC)}°C</div>
        )}
      </div>

      <div className="mt-2 flex gap-6 border-t border-white/15 px-4 py-3">
        {typeof humidity === "number" && <Metric label="Humidity" value={`${Math.round(humidity)}%`} />}
        {typeof windKph === "number" && <Metric label="Wind" value={`${Math.round(windKph)} km/h`} />}
        {typeof uv === "number" && <Metric label="UV" value={`${Math.round(uv)}`} />}
      </div>
    </div>
  );
}

// AG-UI generative UI: renders the server-side get_weather tool call inline in the chat.
// Must be rendered inside the <CopilotKit> provider.
export default function WeatherCardTool() {
  useRenderTool({
    name: "get_weather",
    parameters,
    render: (props) => {
      if (props.status !== "complete") {
        const where = props.parameters?.location ? ` for ${props.parameters.location}` : "";
        return (
          <Pill>
            <span aria-hidden>🌡️</span>
            Fetching the weather{where}…
          </Pill>
        );
      }

      const parsed = parseResult(props.result);
      if (!parsed || parsed.success === false || !parsed.data) {
        return (
          <Pill>
            <span aria-hidden>⚠️</span>
            {parsed?.message ?? "Couldn't load the weather."}
          </Pill>
        );
      }

      return <WeatherCard data={parsed.data} />;
    },
  });
  return null;
}
