import os
import json
import random
import re

from fastapi import FastAPI
from pydantic import BaseModel, Field
from typing import Optional, List
from datetime import datetime, timezone

import httpx


app = FastAPI(
    title="AgriAI Intelligence Engine",
    version="1.0"
)


# ============================================================
# CONFIG
# ============================================================

PERENUAL_API_KEY = os.getenv("PERENUAL_API_KEY", "")
GROQ_API_KEY = os.getenv("GROQ_API_KEY", "")

GROQ_MODEL = os.getenv(
    "GROQ_MODEL",
    "openai/gpt-oss-120b"
)

GROQ_URL = (
    "https://api.groq.com/openai/v1/chat/completions"
)

OLLAMA_URL = os.getenv(
    "OLLAMA_URL",
    "http://localhost:11434/api/chat"
)

OLLAMA_MODEL = os.getenv(
    "OLLAMA_MODEL",
    "llama3.2:1b"
)

PERENUAL_BASE = "https://perenual.com/api"


# ============================================================
# SYSTEM PROMPTS
# ============================================================

SYSTEM_PROMPT = """
You are AgriAI, an agricultural assistant.

Reply in plain text.

Be direct, specific and useful.

IMPORTANT:
If a Perenual reference block is present, distinguish between:
1. facts actually supplied by Perenual
2. general agricultural knowledge

If the user explicitly asks for information "according to Perenual",
you MUST use ONLY information supplied by Perenual.

Do NOT invent missing Perenual information.

Do NOT fill a missing Perenual field with general agricultural knowledge.

If Perenual does not provide the requested information, clearly say that
the requested information is not available in the Perenual data.

If the user did NOT explicitly request Perenual-only information, you may
use general agricultural knowledge, while still preferring verified
Perenual information when available.
"""


SCIENTIFIC_NAME_SYSTEM_PROMPT = """
You identify plant scientific names.

Return ONLY valid JSON.

Do not provide plant-care information.

Do not explain the answer.

Rules:

1. If the supplied plant name is already a scientific/binomial name,
   return it unchanged.
2. Return the accepted scientific name when confidently identifiable.
3. Do not invent a scientific name.
4. If you cannot confidently identify the plant, return unknown.

Required format:

{"scientificName":"Solanum lycopersicum"}

Unknown format:

{"scientificName":"unknown"}
"""


# ============================================================
# REQUEST / RESPONSE MODELS
# ============================================================

class ChatRequest(BaseModel):
    message: str = Field(
        default="",
        description="User question"
    )

    zoneId: Optional[int] = None
    operatorEmail: Optional[str] = None
    conversationId: Optional[str] = None

    # Backwards compatibility
    query: Optional[str] = None

    def text(self) -> str:
        return (
            self.message
            or self.query
            or ""
        ).strip()


class ChatResponse(BaseModel):
    reply: str
    sources: List[str] = []


class EcosystemInsightsResponse(BaseModel):
    status: str
    greenhouseId: str
    timestamp: str
    liveEnvironment: dict
    pestAndInsectAssessment: dict
    agronomyRecommendations: list


# ============================================================
# MOCK ECOSYSTEM DATA
# ============================================================

MOCK_ENVIRONMENT = {
    "externalTemperature": 22.0,
    "externalHumidity": 45.0,
    "precipitation": 0.0,
    "windSpeed": 5.0,
}

MOCK_PEST = {
    "riskLevel": "Low",
    "advisory": "No immediate insect threat detected.",
}

MOCK_RECOMMENDATIONS = [
    "Climate parameters optimal for general crop development "
    "(Leafy greens & tomatoes)."
]


def now_iso() -> str:
    return datetime.now(timezone.utc).isoformat()


# ============================================================
# BASIC TEXT HELPERS
# ============================================================

def clean_text(text: str) -> str:
    return re.sub(
        r"\s+",
        " ",
        (text or "").strip()
    )


def looks_like_scientific_name(text: str) -> bool:
    """
    Detects simple binomial scientific names such as:

    Solanum lycopersicum
    Zea mays
    Lactuca sativa
    """

    text = clean_text(text)

    return bool(
        re.fullmatch(
            r"[A-Z][a-z-]{2,}\s+[a-z-]{2,}",
            text
        )
    )


def is_perenual_request(text: str) -> bool:
    lowered = text.lower()

    keywords = [
        "perenual",
        "according to perenual",
        "from perenual",
        "perenual information",
        "perenual data",
    ]

    return any(
        keyword in lowered
        for keyword in keywords
    )


def is_plant_care_question(text: str) -> bool:
    lowered = text.lower()

    care_terms = [
        "water",
        "watering",
        "irrigation",
        "sunlight",
        "sun",
        "soil",
        "fertilizer",
        "fertilisation",
        "fertilization",
        "temperature",
        "humidity",
        "care",
        "growing",
        "growth",
        "planting",
        "pruning",
        "harvest",
        "propagation",
        "pest",
        "disease",
        "light",
        "soil type",
        "soil requirements",
        "growing requirements",
        "requirements",
    ]

    plant_terms = [
        "plant",
        "crop",
        "tomato",
        "tomatoes",
        "lettuce",
        "maize",
        "corn",
        "pepper",
        "potato",
        "onion",
        "spinach",
        "cucumber",
        "bean",
        "beans",
        "carrot",
        "strawberry",
        "apple",
        "banana",
        "rose",
        "basil",
        "mint",
        "herb",
    ]

    if looks_like_scientific_name(text):
        return True

    has_care = any(
        term in lowered
        for term in care_terms
    )

    has_plant = any(
        term in lowered
        for term in plant_terms
    )

    return has_care or has_plant


# ============================================================
# PLANT NAME EXTRACTION
# ============================================================

def extract_plant_name(
    question: str,
    fallback_crop: Optional[str] = None
) -> str:

    question = clean_text(question)

    # --------------------------------------------------------
    # If the entire message is already a scientific name
    # --------------------------------------------------------

    if looks_like_scientific_name(question):
        return question

    # --------------------------------------------------------
    # Remove common Perenual wording
    # --------------------------------------------------------

    cleaned = re.sub(
        r"\baccording\s+to\s+perenual\b",
        "",
        question,
        flags=re.IGNORECASE
    )

    cleaned = re.sub(
        r"\bfrom\s+perenual\b",
        "",
        cleaned,
        flags=re.IGNORECASE
    )

    cleaned = re.sub(
        r"\bperenual\s+information\b",
        "",
        cleaned,
        flags=re.IGNORECASE
    )

    # --------------------------------------------------------
    # "for tomato"
    # --------------------------------------------------------

    match = re.search(
        r"\bfor\s+([A-Za-z][A-Za-z -]{1,40}?)(?:\?|$)",
        cleaned,
        flags=re.IGNORECASE
    )

    if match:
        candidate = clean_plant_candidate(
            match.group(1)
        )

        if is_plausible_plant_name(candidate):
            return candidate

    # --------------------------------------------------------
    # "about tomato"
    # --------------------------------------------------------

    match = re.search(
        r"\babout\s+([A-Za-z][A-Za-z -]{1,40}?)(?:\?|$)",
        cleaned,
        flags=re.IGNORECASE
    )

    if match:
        candidate = clean_plant_candidate(
            match.group(1)
        )

        if is_plausible_plant_name(candidate):
            return candidate

    # --------------------------------------------------------
    # "of tomato"
    # --------------------------------------------------------

    match = re.search(
        r"\bof\s+([A-Za-z][A-Za-z -]{1,40}?)(?:\?|$)",
        cleaned,
        flags=re.IGNORECASE
    )

    if match:
        candidate = clean_plant_candidate(
            match.group(1)
        )

        if is_plausible_plant_name(candidate):
            return candidate

    # --------------------------------------------------------
    # "plant called tomato"
    # --------------------------------------------------------

    match = re.search(
        r"\bplant\s+(?:called|named)\s+([A-Za-z][A-Za-z -]{1,40}?)(?:\?|$)",
        cleaned,
        flags=re.IGNORECASE
    )

    if match:
        candidate = clean_plant_candidate(
            match.group(1)
        )

        if is_plausible_plant_name(candidate):
            return candidate

    # --------------------------------------------------------
    # "tell me about tomato"
    # --------------------------------------------------------

    match = re.search(
        r"\btell\s+me\s+about\s+([A-Za-z][A-Za-z -]{1,40}?)(?:\?|$)",
        cleaned,
        flags=re.IGNORECASE
    )

    if match:
        candidate = clean_plant_candidate(
            match.group(1)
        )

        if is_plausible_plant_name(candidate):
            return candidate

    # --------------------------------------------------------
    # Common direct crop names
    # --------------------------------------------------------

    common_plants = [
        "tomato",
        "tomatoes",
        "lettuce",
        "maize",
        "corn",
        "pepper",
        "peppers",
        "potato",
        "potatoes",
        "onion",
        "onions",
        "spinach",
        "cucumber",
        "cucumbers",
        "beans",
        "bean",
        "carrot",
        "carrots",
        "strawberry",
        "strawberries",
        "apple",
        "banana",
        "basil",
        "mint",
    ]

    lowered = cleaned.lower()

    for plant in common_plants:
        if re.search(
            rf"\b{re.escape(plant)}\b",
            lowered
        ):
            return plant

    # --------------------------------------------------------
    # Fall back to selected zone crop
    # --------------------------------------------------------

    if fallback_crop:
        return clean_text(fallback_crop)

    return ""


def clean_plant_candidate(candidate: str) -> str:

    candidate = clean_text(candidate)

    candidate = re.sub(
        r"\s+(?:according|based|regarding)\s+to.*$",
        "",
        candidate,
        flags=re.IGNORECASE
    )

    candidate = candidate.strip(
        " .,?!:;\"'"
    )

    return clean_text(candidate)


def is_plausible_plant_name(candidate: str) -> bool:

    if not candidate:
        return False

    lowered = candidate.lower()

    invalid = [
        "the requirements",
        "the watering requirements",
        "watering requirements",
        "requirements",
        "this zone",
        "this crop",
        "the crop",
        "my crop",
        "my plant",
        "the plant",
        "perenual information",
    ]

    return not any(
        item in lowered
        for item in invalid
    )


# ============================================================
# PERENUAL SEARCH
# ============================================================

async def search_species(query: str) -> dict:
    """
    Search Perenual species list.
    """

    if not PERENUAL_API_KEY or not query:
        return {"data": []}

    url = f"{PERENUAL_BASE}/species-list"

    params = {
        "key": PERENUAL_API_KEY,
        "q": query,
    }

    try:

        async with httpx.AsyncClient(
            timeout=20.0
        ) as client:

            response = await client.get(
                url,
                params=params
            )

            if response.status_code != 200:

                print(
                    "⚠️ Perenual search HTTP "
                    f"{response.status_code}"
                )

                return {"data": []}

            return response.json()

    except Exception as e:

        print(
            f"⚠️ Perenual search failed: {e}"
        )

        return {"data": []}


# ============================================================
# PERENUAL MATCHING
# ============================================================

def normalise_name(value: str) -> str:
    return clean_text(
        value
        .lower()
        .replace("-", " ")
    )


def get_scientific_name(species: dict) -> str:

    value = species.get(
        "scientific_name"
    )

    if isinstance(value, list):
        if value:
            return clean_text(
                str(value[0])
            )

    if isinstance(value, str):
        return clean_text(value)

    return ""


def get_common_name(species: dict) -> str:

    value = species.get(
        "common_name"
    )

    if isinstance(value, str):
        return clean_text(value)

    return ""


def find_best_species(
    matches: list,
    query: str
) -> Optional[dict]:

    if not matches:
        return None

    query_normalised = normalise_name(
        query
    )

    # --------------------------------------------------------
    # Exact scientific-name match
    # --------------------------------------------------------

    for species in matches:

        scientific = get_scientific_name(
            species
        )

        if (
            scientific
            and normalise_name(scientific)
            == query_normalised
        ):
            return species

    # --------------------------------------------------------
    # Scientific-name partial match
    # --------------------------------------------------------

    for species in matches:

        scientific = get_scientific_name(
            species
        )

        if (
            scientific
            and query_normalised
            in normalise_name(scientific)
        ):
            return species

    # --------------------------------------------------------
    # Exact common-name match
    # --------------------------------------------------------

    for species in matches:

        common = get_common_name(
            species
        )

        if (
            common
            and normalise_name(common)
            == query_normalised
        ):
            return species

    # --------------------------------------------------------
    # Common-name partial match
    # --------------------------------------------------------

    for species in matches:

        common = get_common_name(
            species
        )

        if (
            common
            and query_normalised
            in normalise_name(common)
        ):
            return species

    # --------------------------------------------------------
    # If query looks scientific, don't blindly select
    # an unrelated Perenual result.
    # --------------------------------------------------------

    if looks_like_scientific_name(query):
        return None

    # --------------------------------------------------------
    # Last resort
    # --------------------------------------------------------

    return matches[0]


# ============================================================
# PERENUAL DETAILS
# ============================================================

async def get_plant_details(
    species_id: int
) -> dict:

    if (
        not PERENUAL_API_KEY
        or not species_id
    ):
        return {}

    url = (
        f"{PERENUAL_BASE}"
        f"/species/details/{species_id}"
    )

    params = {
        "key": PERENUAL_API_KEY
    }

    try:

        async with httpx.AsyncClient(
            timeout=20.0
        ) as client:

            response = await client.get(
                url,
                params=params
            )

            if response.status_code != 200:

                print(
                    "⚠️ Perenual details HTTP "
                    f"{response.status_code}"
                )

                return {}

            return response.json()

    except Exception as e:

        print(
            f"⚠️ Perenual details failed: {e}"
        )

        return {}


# ============================================================
# GROQ SCIENTIFIC NAME
# ============================================================

async def get_scientific_name(
    plant_name: str
) -> str:

    if not GROQ_API_KEY:
        print(
            "ℹ️ GROQ_API_KEY missing — "
            "cannot identify scientific name"
        )

        return ""

    # --------------------------------------------------------
    # Already scientific
    # --------------------------------------------------------

    if looks_like_scientific_name(
        plant_name
    ):
        return plant_name

    headers = {
        "Authorization":
            f"Bearer {GROQ_API_KEY}",

        "Content-Type":
            "application/json",
    }

    payload = {
        "model": GROQ_MODEL,
        "messages": [
            {
                "role": "system",
                "content":
                    SCIENTIFIC_NAME_SYSTEM_PROMPT
            },
            {
                "role": "user",
                "content":
                    f"Plant name: {plant_name}"
            },
        ],
        "temperature": 0,
        "max_tokens": 100,
    }

    try:

        async with httpx.AsyncClient(
            timeout=30.0
        ) as client:

            response = await client.post(
                GROQ_URL,
                headers=headers,
                json=payload
            )

            if response.status_code != 200:

                print(
                    "⚠️ Groq scientific-name HTTP "
                    f"{response.status_code}"
                )

                return ""

            data = response.json()

            content = (
                data
                .get("choices", [{}])[0]
                .get("message", {})
                .get("content", "")
                .strip()
            )

            if not content:
                return ""

            # Remove markdown fences if the model
            # unexpectedly adds them.
            content = re.sub(
                r"^```json\s*",
                "",
                content,
                flags=re.IGNORECASE
            )

            content = re.sub(
                r"^```\s*",
                "",
                content
            )

            content = re.sub(
                r"\s*```$",
                "",
                content
            )

            # Extract JSON object if surrounded by text.
            match = re.search(
                r"\{.*\}",
                content,
                flags=re.DOTALL
            )

            if not match:
                return ""

            parsed = json.loads(
                match.group(0)
            )

            scientific = clean_text(
                str(
                    parsed.get(
                        "scientificName",
                        ""
                    )
                )
            )

            if (
                not scientific
                or scientific.lower()
                == "unknown"
            ):
                return ""

            print(
                f"🔬 Scientific name: "
                f"{plant_name} → {scientific}"
            )

            return scientific

    except Exception as e:

        print(
            "⚠️ Scientific-name lookup failed: "
            f"{e}"
        )

        return ""


# ============================================================
# BUILD PERENUAL CONTEXT
# ============================================================

def build_perenual_context(
    details: dict,
    plant_name: str,
    scientific_name: str
) -> str:

    common_name = details.get(
        "common_name"
    )

    watering = details.get(
        "watering"
    )

    sunlight = details.get(
        "sunlight"
    )

    soil = details.get(
        "soil"
    )

    watering_general_benefit = details.get(
        "watering_general_benefit"
    )

    care_level = details.get(
        "care_level"
    )

    description = details.get(
        "description"
    )

    lines = [
        "[PERENUAL DATA]",
        f"Requested plant: {plant_name}",
        f"Scientific name: "
        f"{scientific_name or 'unknown'}",
        f"Common name: "
        f"{common_name or 'not provided'}",
        f"Watering: "
        f"{watering or 'not provided'}",
        f"Sunlight: "
        f"{sunlight or 'not provided'}",
        f"Soil: "
        f"{soil or 'not provided'}",
        f"Watering benefit: "
        f"{watering_general_benefit or 'not provided'}",
        f"Care level: "
        f"{care_level or 'not provided'}",
        f"Description: "
        f"{description or 'not provided'}",
        "[END PERENUAL DATA]",
    ]

    return "\n".join(lines)


# ============================================================
# GROQ FINAL RESPONSE
# ============================================================

async def call_groq(
    prompt: str,
    perenual_only: bool = False
) -> str:

    if not GROQ_API_KEY:

        print(
            "ℹ️ GROQ_API_KEY missing — "
            "skipping Groq"
        )

        return ""

    system_prompt = SYSTEM_PROMPT

    if perenual_only:

        system_prompt += """

STRICT PERENUAL-ONLY MODE.

The user explicitly asked for information from Perenual.

You MUST NOT provide plant-care information from:
- your own knowledge
- assumptions
- typical agricultural practice
- weather
- sensors
- external web searches
- previous conversations

If the requested field is marked "not provided",
say that Perenual does not provide that information.

Do not substitute a general recommendation.

For example, if Perenual says:
Watering: not provided

and the user asks:
"What are the watering requirements?"

the answer must state that Perenual does not provide
watering requirements for this plant.

Do not then recommend how often to water it.
"""

    headers = {
        "Authorization":
            f"Bearer {GROQ_API_KEY}",

        "Content-Type":
            "application/json",
    }

    payload = {
        "model": GROQ_MODEL,
        "messages": [
            {
                "role": "system",
                "content": system_prompt
            },
            {
                "role": "user",
                "content": prompt
            },
        ],
        "temperature": 0.2,
        "max_tokens": 600,
    }

    try:

        async with httpx.AsyncClient(
            timeout=60.0
        ) as client:

            response = await client.post(
                GROQ_URL,
                headers=headers,
                json=payload
            )

            if response.status_code != 200:

                print(
                    f"⚠️ Groq HTTP "
                    f"{response.status_code}"
                )

                return ""

            data = response.json()

            text = (
                data
                .get("choices", [{}])[0]
                .get("message", {})
                .get("content", "")
                .strip()
            )

            if text:

                print(
                    f"🕐 Groq OK ({GROQ_MODEL})"
                )

            return text

    except Exception as e:

        print(
            f"⚠️ Groq call failed: {e}"
        )

        return ""


# ============================================================
# OLLAMA FALLBACK
# ============================================================

async def call_ollama(
    prompt: str,
    perenual_only: bool = False
) -> str:

    fallback_system = SYSTEM_PROMPT

    if perenual_only:

        fallback_system += """

STRICT PERENUAL-ONLY MODE.

Only use facts explicitly present in the
PERENUAL DATA block.

If a requested field says "not provided",
state that Perenual does not provide it.

Never fill missing plant-care information
with general agricultural knowledge.
"""

    payload = {
        "model": OLLAMA_MODEL,
        "messages": [
            {
                "role": "system",
                "content": fallback_system
            },
            {
                "role": "user",
                "content": prompt
            },
        ],
        "stream": False,
        "options": {
            "temperature": 0.2,
            "num_predict": 400,
            "num_ctx": 2048,
        },
    }

    try:

        async with httpx.AsyncClient(
            timeout=120.0
        ) as client:

            response = await client.post(
                OLLAMA_URL,
                json=payload
            )

            if response.status_code != 200:

                print(
                    f"⚠️ Ollama HTTP "
                    f"{response.status_code}"
                )

                return ""

            data = response.json()

            text = (
                data
                .get("message", {})
                .get("content", "")
                .strip()
            )

            if text:

                print(
                    f"🕐 Ollama OK "
                    f"({OLLAMA_MODEL})"
                )

            return text

    except Exception as e:

        print(
            f"⚠️ Ollama call failed: {e}"
        )

        return ""


# ============================================================
# HARD PERENUAL SAFETY CHECK
# ============================================================

def enforce_perenual_response(
    reply: str,
    details: dict,
    question: str
) -> str:

    if not reply:
        return reply

    if not is_perenual_request(question):
        return reply

    lowered_question = question.lower()

    # --------------------------------------------------------
    # If user explicitly asks about watering and Perenual
    # has no watering field, don't allow Groq to invent it.
    # --------------------------------------------------------

    watering_requested = any(
        word in lowered_question
        for word in [
            "water",
            "watering",
            "irrigation",
        ]
    )

    if watering_requested:

        watering = details.get(
            "watering"
        )

        if not watering:

            return (
                "Perenual does not provide "
                "watering requirements for "
                "this plant in the data returned "
                "by the lookup."
            )

    # --------------------------------------------------------
    # Sunlight
    # --------------------------------------------------------

    sunlight_requested = any(
        word in lowered_question
        for word in [
            "sunlight",
            "sun",
            "light",
        ]
    )

    if sunlight_requested:

        sunlight = details.get(
            "sunlight"
        )

        if not sunlight:

            return (
                "Perenual does not provide "
                "sunlight requirements for "
                "this plant in the data returned "
                "by the lookup."
            )

    # --------------------------------------------------------
    # Soil
    # --------------------------------------------------------

    soil_requested = "soil" in lowered_question

    if soil_requested:

        soil = details.get(
            "soil"
        )

        if not soil:

            return (
                "Perenual does not provide "
                "soil requirements for "
                "this plant in the data returned "
                "by the lookup."
            )

    return reply


# ============================================================
# MAIN CHAT ENDPOINT
# ============================================================

@app.post(
    "/api/ai/chat",
    response_model=ChatResponse
)
async def chat(
    request: ChatRequest
):

    try:

        user_text = request.text()

        if not user_text:

            return ChatResponse(
                reply=(
                    "Please send a question "
                    "in the 'message' field."
                )
            )

        print(
            f"🐛 AI request: "
            f"'{user_text}'"
        )

        sources: List[str] = []

        plant_context = ""

        plant_name = ""

        scientific_name = ""

        plant_details = {}

        # ====================================================
        # PLANT / PERENUAL FLOW
        # ====================================================

        should_lookup_plant = (
            is_plant_care_question(
                user_text
            )
            or is_perenual_request(
                user_text
            )
        )

        if should_lookup_plant:

            # ------------------------------------------------
            # Extract plant from question
            # ------------------------------------------------

            plant_name = extract_plant_name(
                user_text
            )

            print(
                f"🌱 Extracted plant: "
                f"'{plant_name}'"
            )

            if plant_name:

                # --------------------------------------------
                # Groq → scientific name
                # --------------------------------------------

                scientific_name = (
                    await get_scientific_name(
                        plant_name
                    )
                )

                # --------------------------------------------
                # If scientific identification failed,
                # use original plant name for Perenual.
                # --------------------------------------------

                lookup_name = (
                    scientific_name
                    or plant_name
                )

                print(
                    f"🔎 Perenual lookup: "
                    f"'{lookup_name}'"
                )

                # --------------------------------------------
                # Search Perenual
                # --------------------------------------------

                search_result = (
                    await search_species(
                        lookup_name
                    )
                )

                matches = search_result.get(
                    "data",
                    []
                )

                # --------------------------------------------
                # If scientific search failed, retry using
                # original common name.
                # --------------------------------------------

                if (
                    not matches
                    and scientific_name
                    and plant_name
                    and normalise_name(
                        scientific_name
                    )
                    != normalise_name(
                        plant_name
                    )
                ):

                    print(
                        "🔄 Scientific-name search "
                        "returned no results; "
                        "retrying common name"
                    )

                    search_result = (
                        await search_species(
                            plant_name
                        )
                    )

                    matches = search_result.get(
                        "data",
                        []
                    )

                # --------------------------------------------
                # Select best Perenual match
                # --------------------------------------------

                best_match = find_best_species(
                    matches,
                    lookup_name
                )

                if best_match:

                    species_id = best_match.get(
                        "id"
                    )

                    if species_id:

                        plant_details = (
                            await get_plant_details(
                                int(species_id)
                            )
                        )

                        if plant_details:

                            plant_context = (
                                build_perenual_context(
                                    plant_details,
                                    plant_name,
                                    scientific_name
                                    or get_scientific_name(
                                        best_match
                                    )
                                )
                            )

                            sources.append(
                                "perenual"
                            )

                            print(
                                "✅ Perenual matched: "
                                f"{plant_name}"
                            )

                            print(
                                f"   Scientific: "
                                f"{scientific_name or get_scientific_name(best_match)}"
                            )

                            print(
                                f"   Watering: "
                                f"{plant_details.get('watering') or 'not provided'}"
                            )

                            print(
                                f"   Sunlight: "
                                f"{plant_details.get('sunlight') or 'not provided'}"
                            )

                        else:

                            print(
                                "⚠️ Perenual species "
                                "matched but details "
                                "were empty"
                            )

                    else:

                        print(
                            "⚠️ Perenual result "
                            "did not contain an ID"
                        )

                else:

                    print(
                        "⚠️ Perenual found "
                        "no reliable match"
                    )

            else:

                print(
                    "⚠️ Could not extract "
                    "a plant name"
                )

        # ====================================================
        # PERENUAL-ONLY MODE
        # ====================================================

        perenual_only = (
            is_perenual_request(
                user_text
            )
        )

        # ====================================================
        # BUILD FINAL PROMPT
        # ====================================================

        if plant_context:

            prompt = (
                f"{plant_context}\n\n"
                f"User question:\n"
                f"{user_text}"
            )

        else:

            prompt = (
                f"User question:\n"
                f"{user_text}"
            )

        # ====================================================
        # LLM
        # ====================================================

        reply = await call_groq(
            prompt,
            perenual_only=perenual_only
        )

        if reply:

            sources.append(
                "groq"
            )

        else:

            print(
                "⚠️ Groq unavailable — "
                "falling back to Ollama"
            )

            reply = await call_ollama(
                prompt,
                perenual_only=perenual_only
            )

            if reply:
                sources.append(
                    "ollama"
                )

        # ====================================================
        # HARD ENFORCEMENT
        # ====================================================

        if (
            perenual_only
            and plant_details
        ):

            reply = enforce_perenual_response(
                reply,
                plant_details,
                user_text
            )

        # ====================================================
        # EMPTY RESPONSE FALLBACK
        # ====================================================

        if not reply:

            reply = (
                "AgriAI could not generate "
                "a response at this time."
            )

        # ====================================================
        # FINAL LOG
        # ====================================================

        print(
            "📤 Final sources: "
            f"[{', '.join(sources)}]"
        )

        return ChatResponse(
            reply=reply,
            sources=sources
        )

    except Exception as e:

        print(
            f"❌ AI endpoint error: {e}"
        )

        return ChatResponse(
            reply=(
                "AgriAI is currently offline."
            )
        )


# ============================================================
# ECOSYSTEM INSIGHTS
# ============================================================

@app.get(
    "/api/ai/ecosystem-insights/{greenhouse_id}",
    response_model=EcosystemInsightsResponse
)
async def ecosystem_insights(
    greenhouse_id: str
):

    temp_variation = random.uniform(
        -1.0,
        1.0
    )

    env = MOCK_ENVIRONMENT.copy()

    env["externalTemperature"] = round(
        env["externalTemperature"]
        + temp_variation,
        1
    )

    return EcosystemInsightsResponse(
        status="success",
        greenhouseId=greenhouse_id,
        timestamp=now_iso(),
        liveEnvironment=env,
        pestAndInsectAssessment=MOCK_PEST,
        agronomyRecommendations=MOCK_RECOMMENDATIONS,
    )


# ============================================================
# RUN
# ============================================================

if __name__ == "__main__":

    import uvicorn

    uvicorn.run(
        app,
        host="0.0.0.0",
        port=8000
    )