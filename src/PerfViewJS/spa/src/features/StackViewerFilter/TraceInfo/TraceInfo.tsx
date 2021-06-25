import React, { useEffect, useState } from "react";
import { Container, Row, Col } from "react-grid-system";
import { getTheme, Text } from "@fluentui/react";
import { FontAwesomeIcon } from "@fortawesome/react-fontawesome";
import {
  IconDefinition,
  faMicrochip,
  faMemory,
  faClock,
  faMousePointer,
  faHourglassHalf,
  faCalendarWeek,
} from "@fortawesome/free-solid-svg-icons";
import { faWindows } from "@fortawesome/free-brands-svg-icons";
import { SizeProp } from "@fortawesome/fontawesome-svg-core";
import dayjs from "dayjs";
import { useDataFileContext } from "context/DataFileContext";
import { Loading } from "components/Loading";
dayjs().format();

const theme = getTheme();
interface ITraceState {
  traceInfo: ITraceInfo | null;
  loading: boolean;
}

interface ITraceInfo {
  machineName: string;
  operatingSystemName: string;
  operatingSystemBuildNumber: string;
  utcDiff: number;
  utcOffsetCurrentProcess: number;
  bootTime: string;
  startTime: string;
  endTime: string;
  duration: number;
  processorSpeed: number;
  numberOfProcessors: number;
  memorySize: number;
  pointerSize: number;
  sampleProfileInterval: number;
  totalEvents: number;
  lostEvents: number;
  fileSize: number;
}

const rowStyle = {
  borderTop: `1px dotted ${theme.semanticColors.menuDivider}`,
  paddingTop: 15,
  marginTop: 15,
};

const TraceInfo: React.FC = () => {
  const [trace, setTraceInfo] = useState<ITraceState>({ loading: true, traceInfo: null });
  const { dataFile } = useDataFileContext();
  useEffect(() => {
    fetch(`/api/traceinfo?filename=${dataFile}`)
      .then((res) => res.json())
      .then((data) => {
        setTraceInfo({ traceInfo: data, loading: false });
      });
  }, []);

  const renderTraceInfo = () => {
    const { traceInfo } = trace;
    if (traceInfo === null) return <Loading />;
    const ProcessorComponent = [];
    for (let i = 1; i < traceInfo.numberOfProcessors + 1; i++) {
      ProcessorComponent.push(
        <FontAwesomeIcon
          key={`${i}-fa`}
          icon={faMicrochip}
          color={theme.semanticColors.accentButtonBackground}
          style={{ padding: "3px" }}
        />
      );
      if (i % 4 === 0 && i !== traceInfo.numberOfProcessors) {
        ProcessorComponent.push(<br key={`${i}-br`} />);
      }
    }

    return (
      <Container fluid>
        <Row style={rowStyle}>
          <Col sm={6} md={3} lg={2} style={{ textAlign: "center" }}>
            {FaComponent(faWindows)}
          </Col>
          <Col sm={6} md={6} lg={5}>
            <Text variant={"xSmall"} block>{`System Info`}</Text>
            <Text variant={"large"} block>
              {/* {traceInfo.operatingSystemName || "Windows 10 Enterprise"} */}
              {traceInfo.operatingSystemName || "Not available"}
            </Text>
          </Col>
          <Col sm={6} md={9} lg={5} offset={{ sm: 6, md: 3, lg: 0 }}>
            <Text variant={"xSmall"} block>{`Machine name`}</Text>
            <Text variant={"large"} block>
              {/* {traceInfo.machineName || "very-long-name-for-testing"} */}
              {traceInfo.machineName || "Not available"}
            </Text>
          </Col>
          <Col sm={6} md={9} lg={12} offset={{ sm: 6, md: 3, lg: 2 }}>
            <Text variant={"xSmall"} block>{`Operating System build number`}</Text>
            <Text variant={"large"} block>
              {/* {traceInfo.operatingSystemBuildNumber || "18362.1.amd64.19h1_release.190318-1202"} */}
              {traceInfo.operatingSystemBuildNumber || "Not available"}
            </Text>
          </Col>
        </Row>
        <Row style={rowStyle}>
          <Col sm={6} md={3} lg={2} style={{ textAlign: "center" }}>
            {ProcessorComponent}
          </Col>
          <Col sm={6} md={3} lg={2}>
            <Text variant={"xSmall"} block>{`CPU Frequency (${traceInfo.numberOfProcessors})`}</Text>
            <Text variant={"large"} block>{`${traceInfo.processorSpeed}MHz`}</Text>
          </Col>
          <Col sm={6} md={3} lg={2} style={{ textAlign: "center" }}>
            {FaComponent(faMemory)}
          </Col>
          <Col sm={6} md={3} lg={2}>
            <Text variant={"xSmall"} block>{`Memory Size`}</Text>
            <Text variant={"large"}>{traceInfo.memorySize}MB</Text>
          </Col>
          <Col sm={6} md={3} lg={2} style={{ textAlign: "center" }}>
            {FaComponent(faMousePointer)}
          </Col>
          <Col sm={6} md={3} lg={2}>
            <Text variant={"xSmall"} block>
              {"Pointer size"}
            </Text>
            <Text variant={"large"} block>{`${traceInfo.pointerSize}`}</Text>
          </Col>
        </Row>
        <Row style={rowStyle}>
          <Col sm={6} md={3} lg={2} style={{ textAlign: "center" }}>
            {FaComponent(faHourglassHalf)}
          </Col>
          <Col sm={6} md={9} lg={4}>
            <Text variant={"xSmall"} block>{`Trace Time Span`}</Text>
            <Text variant={"large"} block>{`${dayjs(traceInfo.startTime)}`}</Text>
            <Text variant={"large"} block>{`${dayjs(traceInfo.endTime)}`}</Text>
          </Col>
          <Col sm={6} md={4} lg={4} offset={{ sm: 6, md: 3, lg: 0 }}>
            <Text variant={"xSmall"} block>{`Trace duration`}</Text>
            <Text variant={"large"} block>{`${traceInfo.duration.toFixed(2)}sec`}</Text>
          </Col>
        </Row>
        <Row style={rowStyle}>
          <Col sm={6} md={3} lg={2} style={{ textAlign: "center" }}>
            {FaComponent(faCalendarWeek)}
          </Col>
          <Col sm={6} md={3} lg={2}>
            <Text variant={"xSmall"} block>{`Events Captured`}</Text>
            <Text variant={"large"} block>{`${traceInfo.totalEvents}`}</Text>
          </Col>
          <Col sm={6} md={3} lg={2} offset={{ sm: 6, md: 0, lg: 0 }}>
            <Text variant={"xSmall"} block>{`Events Lost`}</Text>
            <Text variant={"large"} block>{`${traceInfo.lostEvents}`}</Text>
          </Col>
          <Col sm={6} md={3} lg={2} offset={{ sm: 6, md: 3, lg: 0 }}>
            <Text variant={"xSmall"} block>{`Sample Interval`}</Text>
            <Text variant={"large"} block>{`${traceInfo.sampleProfileInterval} MSec`}</Text>
          </Col>
          <Col sm={6} md={3} lg={2} offset={{ sm: 6, md: 0, lg: 0 }}>
            <Text variant={"xSmall"} block>{`Trace file size`}</Text>
            <Text variant={"large"} block>{`${traceInfo.fileSize.toFixed(2)}MB`}</Text>
          </Col>
        </Row>
        <Row style={rowStyle}>
          <Col sm={6} md={3} lg={2} style={{ textAlign: "center" }}>
            {FaComponent(faClock)}
          </Col>
          <Col sm={6} md={9} lg={4}>
            <Text variant={"xSmall"} block>{`OS Boot time`}</Text>
            <Text variant={"large"} block>{`${dayjs(traceInfo.bootTime)}`}</Text>
          </Col>
          <Col sm={6} md={6} lg={2} offset={{ sm: 6, md: 3, lg: 0 }}>
            <Text variant={"xSmall"} block>{`UTC offset where data was collected`}</Text>
            <Text variant={"large"} block>{`${traceInfo.utcDiff}`}</Text>
          </Col>
          <Col sm={6} md={6} lg={2} offset={{ sm: 6, md: 3, lg: 0 }}>
            <Text variant={"xSmall"} block>{`UTC offset where PerfView is running`}</Text>
            <Text variant={"large"} block>{`${traceInfo.utcOffsetCurrentProcess}`}</Text>
          </Col>
        </Row>
      </Container>
    );
  };

  const FaComponent = (icon: IconDefinition, size: SizeProp = "3x") => (
    <FontAwesomeIcon icon={icon} color={theme.semanticColors.accentButtonBackground} size={size} />
  );

  return (
    <Container>
      <Row>
        <Col>
          <Text variant={"xLarge"}>Trace Info</Text>
        </Col>
      </Row>
      <Row justify="center" align="center">
        <Col>{renderTraceInfo()}</Col>
      </Row>
    </Container>
  );
};

export { TraceInfo };
