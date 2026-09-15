import { Container } from '@cloudflare/containers';
/**
 * 도킹 계산 서비스(server/docking, FastAPI + Vina)를 Cloudflare Containers로 감싸는 Durable Object.
 * 컨테이너는 공개 주소가 없고 이 클래스를 통해서만 호출된다. 작업 상태가 프로세스 메모리에 있으므로
 * 인스턴스는 항상 하나(getContainer 이름 고정)이며, 유휴 시간이 지나면 잠들고 다음 요청에서 다시 뜬다.
 */
export class DockingContainer extends Container {
  defaultPort = 8000;
  sleepAfter = '15m';
  constructor(ctx, env) {
    super(ctx, env);
    // 컨테이너 쪽 토큰 검사는 값이 있을 때만 켜진다. Worker→컨테이너 구간은 외부에 열려 있지 않다.
    this.envVars = env.DOCKING_SERVICE_TOKEN ? { DOCKING_SERVICE_TOKEN: env.DOCKING_SERVICE_TOKEN } : {};
  }
}
