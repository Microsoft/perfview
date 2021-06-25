import React, { useState } from "react";
import base64url from "base64url";
import { ISpinButtonStyles, PrimaryButton, SpinButton, Stack, TextField } from "@fluentui/react";
import { Col, Row } from "react-grid-system";
import { useRouteKeyContext } from "context/RouteContext";
import { GroupingPatternsExampleComponent } from "./GroupingPatternsExample";
import { constructAPICacheKeyFromRouteKey } from "common/Utility";

const defaultSymbolsMinCount = "50";
const updateButtonStyle = {
  root: {
    marginTop: 12,
  },
};

//?azure theme spin button is different from TextField with labels..
const spinButtonStyles: Partial<ISpinButtonStyles> = {
  labelWrapper: { height: 27 },
  spinButtonWrapper: { height: 24 },
};

const StackViewerFilter: React.FC = () => {
  const { routeKey, setRouteKey } = useRouteKeyContext();
  const data = JSON.parse(base64url.decode(routeKey));

  const [lookupwarmsymbolsResponse, setLookupwarmsymbolsResponse] = useState("");

  const [relativeStartTime, setRelativeStartTime] = useState(data.d);
  const [relativeEndTime, setRelativeEndTime] = useState(data.e);
  const [groupingPatterns, setGroupingPatterns] = useState(data.f);
  const [foldingPatterns, setFoldingPatterns] = useState(data.g);
  const [includePatterns, setIncludePatterns] = useState(data.h);
  const [excludePatterns, setExcludePatterns] = useState(data.i);
  const [foldCount] = useState(data.j);
  const [drillIntoKey] = useState(data.k);

  const [lookupWarmSymbolsMinCount, setLookupWarmSymbolsMinCount] = useState<number>(
    parseInt(defaultSymbolsMinCount, 10)
  );

  const higherOrderStringEventHandler =
    (setStateAction: React.Dispatch<React.SetStateAction<string>>) =>
    (event: React.FormEvent<HTMLInputElement | HTMLTextAreaElement>, newValue?: string) => {
      if (newValue) setStateAction(newValue);
    };

  const handleLookupWarmSymbolsMinCount = (event: React.SyntheticEvent<HTMLElement>, newValue?: string) => {
    if (newValue) setLookupWarmSymbolsMinCount(parseInt(newValue, 10));
  };

  const onValidate = (value: string): string | void => {
    return Number.isInteger(parseInt(value)) ? value : defaultSymbolsMinCount;
  };

  const handleOnClick = (e: React.MouseEvent<HTMLButtonElement>) => {
    e.preventDefault();
    const oldRouteKey = JSON.parse(base64url.decode(routeKey));
    const newRouteKeyJsonString = JSON.stringify({
      a: oldRouteKey.a,
      b: oldRouteKey.b,
      c: -1,
      d: relativeStartTime,
      e: relativeEndTime,
      f: groupingPatterns,
      g: foldingPatterns,
      h: includePatterns,
      i: excludePatterns,
      j: foldCount,
      k: drillIntoKey,
      l: oldRouteKey.l,
    });
    setRouteKey(base64url.encode(newRouteKeyJsonString));
  };

  const handleLookupWarmSymbols = () => {
    fetch(`/api/lookupwarmsymbols?minCount=${lookupWarmSymbolsMinCount}&${constructAPICacheKeyFromRouteKey(routeKey)}`)
      .then((res) => res.json())
      .then((data) => {
        setLookupwarmsymbolsResponse(data);
        // toast.success(() => (
        //   <Container>
        //     <Row>
        //       <Text block>{data}</Text>
        //     </Row>
        //   </Container>
        // ));
        setRouteKey(routeKey);
      });
  };

  return (
    <>
      <Row justify="start">
        <Col sm={6} md={6} lg={3}>
          <TextField
            label={"Grouping Patterns (Regex)"}
            value={groupingPatterns}
            onChange={higherOrderStringEventHandler(setGroupingPatterns)}
          />
          <TextField
            label={"Relative Start Time (ms)"}
            value={relativeStartTime}
            onChange={higherOrderStringEventHandler(setRelativeStartTime)}
          />
          <TextField
            label={"Include Patterns (Regex)"}
            value={includePatterns}
            onChange={higherOrderStringEventHandler(setIncludePatterns)}
          />
          <PrimaryButton styles={updateButtonStyle} onClick={handleOnClick}>
            Update
          </PrimaryButton>
        </Col>
        <Col sm={6} md={6} lg={3}>
          <TextField
            label={"Folding Patterns (Regex)"}
            value={foldingPatterns}
            onChange={higherOrderStringEventHandler(setFoldingPatterns)}
          />
          <TextField
            label={"Relative End Time (ms)"}
            value={relativeEndTime}
            onChange={higherOrderStringEventHandler(setRelativeEndTime)}
          />
          <TextField
            label={"Exclude Patterns (Regex)"}
            value={excludePatterns}
            onChange={higherOrderStringEventHandler(setExcludePatterns)}
          />
        </Col>
        <Col sm={12} md={12} lg={6}>
          <GroupingPatternsExampleComponent />
        </Col>
      </Row>
      <Row style={{ paddingTop: 12 }}>
        <Col sm={3} md={3}>
          <Stack>
            <PrimaryButton styles={{ root: { fontSize: "0.75rem" } }} onClick={handleLookupWarmSymbols}>
              Lookup # of Symbols (min samples)
            </PrimaryButton>
          </Stack>
        </Col>
        <Col sm={3} md={3}>
          <SpinButton
            defaultValue={defaultSymbolsMinCount}
            onChange={handleLookupWarmSymbolsMinCount}
            min={1}
            max={1000}
            step={1}
            onValidate={onValidate}
            styles={spinButtonStyles}
          />
        </Col>
      </Row>
      {lookupwarmsymbolsResponse && (
        <Row style={{ paddingTop: 12 }}>
          <Col>
            <TextField rows={10} readOnly multiline value={lookupwarmsymbolsResponse} />
          </Col>
        </Row>
      )}
    </>
  );
};

export { StackViewerFilter };
