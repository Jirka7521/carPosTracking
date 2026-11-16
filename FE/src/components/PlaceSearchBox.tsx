// ============================================================
// PlaceSearchBox — "find a place" above the place-rule map: type an address or
// the name of a place, pick a suggestion, and the circle moves there.
//
// Google Places Autocomplete (New), called directly (AutocompleteSuggestion)
// rather than through Google's own widget, so it looks like the rest of the
// form and stays an ordinary accessible combobox. LocationPickerMap mounts it
// only once the viewer has agreed to load maps: the typed text goes to Google.
//
// Every request is billed, so it asks sparingly:
//   • suggestions are fetched 1.2 s after the last keystroke, or at once on
//     Enter, and only from 3 characters on — never per keystroke;
//   • one session token covers a search from the first request to the pick,
//     so Google bills it as one session;
//   • the pick reads only the place's `location`, which keeps the lookup in the
//     Place Details Essentials SKU. The rule's name comes from the suggestion's
//     own text, not from `displayName`, which would make it a Pro lookup.
// ============================================================

import { useEffect, useId, useRef, useState } from 'react'
import type { ChangeEvent, KeyboardEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { loadPlacesLibrary } from '../utils/googleMapsLoader'
import type { PickedPoint } from './LocationPickerMap'

// More than a second: long enough that a word is finished before Google is
// asked, so a search typically costs one to three requests.
const SEARCH_DELAY_MS: number = 1200
const MIN_QUERY_LENGTH: number = 3
const MAX_SUGGESTIONS: number = 5

type Suggestion = {
  id: string
  main: string
  secondary: string
  prediction: google.maps.places.PlacePrediction
}

type SearchStatus = 'idle' | 'loading' | 'ready' | 'failed'

export type PlaceSearchBoxProps = {
  apiKey: string
  // The area to prefer (the map's viewport), or null when there is none yet.
  getLocationBias: () => google.maps.LatLngBounds | null
  // A place was picked: its location, and a short name for it.
  onPick: (point: PickedPoint, name: string) => void
  disabled: boolean
}

export function PlaceSearchBox({ apiKey, getLocationBias, onPick, disabled }: PlaceSearchBoxProps) {
  const { t, i18n } = useTranslation('trips')
  const inputId: string = useId()
  const listId: string = useId()
  const [query, setQuery] = useState<string>('')
  const [suggestions, setSuggestions] = useState<Suggestion[]>([])
  const [status, setStatus] = useState<SearchStatus>('idle')
  const [isOpen, setIsOpen] = useState<boolean>(false)
  const [activeIndex, setActiveIndex] = useState<number>(-1)

  const timerRef = useRef<number | undefined>(undefined)
  // The text in the box right now, for telling a late answer from a current one.
  const queryRef = useRef<string>('')
  // The text last sent to Google, so the same text is never paid for twice.
  const searchedRef = useRef<string | null>(null)
  const tokenRef = useRef<google.maps.places.AutocompleteSessionToken | null>(null)

  useEffect(() => () => window.clearTimeout(timerRef.current), [])

  const language: string = i18n.resolvedLanguage ?? i18n.language

  async function search(text: string): Promise<void> {
    window.clearTimeout(timerRef.current)
    const input: string = text.trim()
    if (input.length < MIN_QUERY_LENGTH || disabled) {
      return
    }
    if (input === searchedRef.current) {
      setIsOpen(true)
      return
    }

    searchedRef.current = input
    setStatus('loading')
    setIsOpen(true)
    try {
      const places: google.maps.PlacesLibrary = await loadPlacesLibrary(apiKey)
      if (tokenRef.current === null) {
        tokenRef.current = new places.AutocompleteSessionToken()
      }
      const request: google.maps.places.AutocompleteRequest = {
        input,
        sessionToken: tokenRef.current,
        language,
      }
      const bias: google.maps.LatLngBounds | null = getLocationBias()
      if (bias !== null) {
        request.locationBias = bias
      }

      const response: { suggestions: google.maps.places.AutocompleteSuggestion[] } =
        await places.AutocompleteSuggestion.fetchAutocompleteSuggestions(request)
      // Typed on since: this answer is for text no longer in the box.
      if (queryRef.current.trim() !== input) {
        if (searchedRef.current === input) {
          searchedRef.current = null
        }
        return
      }
      const found: Suggestion[] = response.suggestions
        .flatMap((suggestion) => (suggestion.placePrediction === null ? [] : [toSuggestion(suggestion.placePrediction)]))
        .slice(0, MAX_SUGGESTIONS)
      setSuggestions(found)
      setActiveIndex(-1)
      setStatus('ready')
    } catch {
      if (queryRef.current.trim() === input) {
        // Not remembered as searched, so Enter tries again.
        searchedRef.current = null
        setSuggestions([])
        setStatus('failed')
      }
    }
  }

  async function pick(suggestion: Suggestion): Promise<void> {
    setIsOpen(false)
    setActiveIndex(-1)
    try {
      const place: google.maps.places.Place = suggestion.prediction.toPlace()
      // `location` only — see the header for why not displayName.
      await place.fetchFields({ fields: ['location'] })
      const location: google.maps.LatLng | null | undefined = place.location
      if (!location) {
        throw new Error('The place has no location.')
      }

      const shown: string = suggestion.secondary === '' ? suggestion.main : `${suggestion.main}, ${suggestion.secondary}`
      queryRef.current = shown
      searchedRef.current = shown.trim()
      setQuery(shown)
      setSuggestions([])
      setStatus('idle')
      // The session ends with the pick; the next search starts a new one.
      tokenRef.current = null
      onPick({ latitude: location.lat(), longitude: location.lng() }, suggestion.main)
    } catch {
      setStatus('failed')
      setIsOpen(true)
    }
  }

  function clear(): void {
    window.clearTimeout(timerRef.current)
    queryRef.current = ''
    searchedRef.current = null
    tokenRef.current = null
    setQuery('')
    setSuggestions([])
    setStatus('idle')
    setIsOpen(false)
    setActiveIndex(-1)
  }

  function handleChange(event: ChangeEvent<HTMLInputElement>): void {
    const text: string = event.target.value
    queryRef.current = text
    setQuery(text)
    setActiveIndex(-1)
    window.clearTimeout(timerRef.current)

    if (text.trim().length < MIN_QUERY_LENGTH) {
      searchedRef.current = null
      setSuggestions([])
      setStatus('idle')
      setIsOpen(false)
      return
    }
    timerRef.current = window.setTimeout(() => {
      void search(text)
    }, SEARCH_DELAY_MS)
  }

  function handleKeyDown(event: KeyboardEvent<HTMLInputElement>): void {
    const count: number = suggestions.length
    switch (event.key) {
      case 'ArrowDown':
        event.preventDefault()
        if (count > 0) {
          setIsOpen(true)
          setActiveIndex((index) => (index + 1) % count)
        }
        break
      case 'ArrowUp':
        event.preventDefault()
        if (count > 0) {
          setIsOpen(true)
          setActiveIndex((index) => (index <= 0 ? count - 1 : index - 1))
        }
        break
      case 'Enter': {
        // Never submits the dialog's form: Enter here means "search" or "pick".
        event.preventDefault()
        const active: Suggestion | undefined = isOpen && activeIndex >= 0 ? suggestions[activeIndex] : undefined
        if (active !== undefined) {
          void pick(active)
        } else {
          void search(query)
        }
        break
      }
      case 'Escape':
        // Closes the suggestions only; with none open, Escape closes the dialog
        // as usual (its listener is on window, past React's root).
        if (isOpen) {
          event.preventDefault()
          event.stopPropagation()
          setIsOpen(false)
          setActiveIndex(-1)
        }
        break
      default:
        break
    }
  }

  const showList: boolean = isOpen && status === 'ready' && suggestions.length > 0
  const activeId: string | undefined = showList && activeIndex >= 0 ? `${listId}-${activeIndex}` : undefined

  return (
    <div className="form-field place-search">
      <label htmlFor={inputId}>{t('locationRule.search.label')}</label>
      <div className="place-search-control">
        <input
          id={inputId}
          className="form-input"
          type="text"
          role="combobox"
          aria-autocomplete="list"
          aria-expanded={showList}
          aria-controls={listId}
          aria-activedescendant={activeId}
          autoComplete="off"
          spellCheck={false}
          value={query}
          placeholder={t('locationRule.search.placeholder')}
          onChange={handleChange}
          onKeyDown={handleKeyDown}
          onBlur={() => setIsOpen(false)}
          onFocus={() => {
            if (suggestions.length > 0) {
              setIsOpen(true)
            }
          }}
          disabled={disabled}
        />
        {query !== '' && !disabled ? (
          <button
            type="button"
            className="place-search-clear"
            aria-label={t('locationRule.search.clear')}
            title={t('locationRule.search.clear')}
            onClick={clear}
          >
            ✕
          </button>
        ) : null}

        {isOpen ? (
          <div className="place-search-popup">
            {status === 'loading' ? (
              <p className="place-search-status" aria-live="polite">{t('locationRule.search.loading')}</p>
            ) : status === 'failed' ? (
              <p className="place-search-status" aria-live="polite">{t('locationRule.search.failed')}</p>
            ) : status === 'ready' && suggestions.length === 0 ? (
              <p className="place-search-status" aria-live="polite">{t('locationRule.search.noResults')}</p>
            ) : null}
            {showList ? (
              <>
                <ul className="place-search-list" role="listbox" id={listId} aria-label={t('locationRule.search.label')}>
                  {suggestions.map((suggestion, index) => (
                    <li
                      key={suggestion.id}
                      id={`${listId}-${index}`}
                      role="option"
                      aria-selected={index === activeIndex}
                      className="place-search-option"
                      // Keeps the focus in the input, so its blur does not close
                      // the list before the click lands.
                      onMouseDown={(event) => event.preventDefault()}
                      onMouseEnter={() => setActiveIndex(index)}
                      onClick={() => {
                        void pick(suggestion)
                      }}
                    >
                      <span className="place-search-main">{suggestion.main}</span>
                      {suggestion.secondary !== '' ? (
                        <span className="place-search-secondary">{suggestion.secondary}</span>
                      ) : null}
                    </li>
                  ))}
                </ul>
                <p className="place-search-attribution">{t('locationRule.search.attribution')}</p>
              </>
            ) : null}
          </div>
        ) : null}
      </div>
    </div>
  )
}

function toSuggestion(prediction: google.maps.places.PlacePrediction): Suggestion {
  return {
    id: prediction.placeId,
    main: prediction.mainText?.text ?? prediction.text.text,
    secondary: prediction.secondaryText?.text ?? '',
    prediction,
  }
}
