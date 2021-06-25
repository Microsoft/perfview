import React, { useEffect, useState } from "react";
import { RouteComponentProps } from "react-router-dom";
import { Module, Process } from "common/Interfaces";
import { ModuleList } from "features/ModuleList/ModuleList";
import { useDataFileContext } from "context/DataFileContext";
import { ProcessList } from "./ProcessList";
import { DetailsList, CheckboxVisibility, Pivot, PivotItem } from "@fluentui/react";
import { Col, Container, Row } from "react-grid-system";
import { ThreadListColDef } from "./ThreadListColDef";
import dayjs from "dayjs";
dayjs().format();

interface MatchParams {
  processIndex: string;
}

export interface IProcessInfoProps extends RouteComponentProps<MatchParams> {}

interface Thread {
  threadId: number;
  threadIndex: number;
  cpumSec: number;
  startTime: string;
  startTimeRelativeMSec: number;
  endTime: string;
  endTimeRelativeMSec: number;
}

interface DetailedProcessInfo {
  processInfo: Process;
  threads: Thread[];
  modules: Module[];
}

const ProcessInfo: React.FC<IProcessInfoProps> = (props) => {
  const {
    match: {
      params: { processIndex },
    },
  } = props;
  const [processInfo, setProcessInfo] = useState<DetailedProcessInfo>({
    processInfo: {
      id: -1,
      commandLine: "",
      cpumSec: 0,
      name: "",
      parentId: -1,
      processId: -1,
    },
    threads: [],
    modules: [],
  });
  const { dataFile } = useDataFileContext();

  useEffect(() => {
    fetch(`/api/processinfo?filename=${dataFile}&processIndex=${processIndex}`)
      .then((res) => res.json())
      .then((data: DetailedProcessInfo) => {
        data.threads = data.threads.map((thread: Thread) => {
          return {
            ...thread,
            startTime: dayjs(thread.startTime).toString(),
            endTime: dayjs(thread.endTime).toString(),
          };
        });
        setProcessInfo(data);
      });
  }, [processIndex, dataFile]);

  return (
    <>
      <ProcessList processIndex={parseInt(processIndex, 10)} />
      <Container>
        <Row>
          <Col>
            <Pivot>
              <PivotItem headerText={"Module list"}>
                <ModuleList />
              </PivotItem>
              <PivotItem headerText={"Thread list"}>
                <Container>
                  <Row>
                    <Col>
                      <DetailsList
                        checkboxVisibility={CheckboxVisibility.hidden}
                        setKey={"key"}
                        compact={true}
                        items={processInfo.threads}
                        columns={ThreadListColDef}
                      />
                    </Col>
                  </Row>
                </Container>
              </PivotItem>
            </Pivot>
          </Col>
        </Row>
      </Container>
    </>
  );
};

export { ProcessInfo };
