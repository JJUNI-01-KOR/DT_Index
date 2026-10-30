"""fake_panel — 키보드로 Unity 제어 패널 버튼을 흉내 낸다. 담당 D · scope `fake`

Unity 제어 패널이 생기기 전까지 factory_manager를 시험할 때 쓴다.
토픽 이름은 SRS v3.1 4.4를 따른다.
factory_manager는 E_STOP으로 시작하므로 처음에 e(해제)를 한 번 누른 뒤 s(시작)를 누른다.

실행 (fake_unity_node와 다른 터미널에서)
    ros2 run dt_fake_unity fake_panel
"""
import time

import rclpy
from rclpy.node import Node
from std_msgs.msg import Bool, String

DEVICES = ('arm_1', 'arm_2', 'arm_3', 'conveyor')

HELP = """\
 s = [시작]            /factory/start true
 e = [비상정지]·[해제]  /factory/emergency_stop true ↔ false (처음 누르면 해제)
 f = [고장 주입]        /factory/inject_fault <장비>
 c = [조치 완료]        /factory/fault_clear <장비>
 h = 도움말   q = 끝"""


def ask_device(prompt):
    device = input(f'  {prompt} ({", ".join(DEVICES)}): ').strip()
    if device not in DEVICES:
        print(f'  ✖ 장비는 {", ".join(DEVICES)} 중 하나')
        return None
    return device


def main(args=None):
    rclpy.init(args=args)
    node = Node('fake_panel')
    start = node.create_publisher(Bool, '/factory/start', 10)
    estop = node.create_publisher(Bool, '/factory/emergency_stop', 10)
    fault = node.create_publisher(String, '/factory/inject_fault', 10)
    clear = node.create_publisher(String, '/factory/fault_clear', 10)
    log = node.get_logger()

    time.sleep(1.0)   # 막 만든 publisher는 첫 메시지를 놓칠 수 있어 연결될 시간을 준다
    stopped = True    # factory_manager는 E_STOP으로 시작한다 (SRS v3.1 FR-17)
    print(HELP)
    try:
        while rclpy.ok():
            key = input('> ').strip().lower()
            if key == 's':
                start.publish(Bool(data=True))
                log.info('[시작] /factory/start true')
            elif key == 'e':
                stopped = not stopped
                estop.publish(Bool(data=stopped))
                log.info(f'[비상정지] /factory/emergency_stop {str(stopped).lower()}'
                         f' ({"누름" if stopped else "해제"})')
            elif key == 'f':
                device = ask_device('고장 주입 대상')
                if device:
                    fault.publish(String(data=device))
                    log.info(f'[고장 주입] /factory/inject_fault {device}')
            elif key == 'c':
                device = ask_device('조치한 장비')
                if device:
                    clear.publish(String(data=device))
                    log.info(f'[조치 완료] /factory/fault_clear {device}')
            elif key in ('h', '?'):
                print(HELP)
            elif key == 'q':
                break
            elif key:
                print('  ? h를 누르면 도움말')
    except (KeyboardInterrupt, EOFError):
        pass
    finally:
        node.destroy_node()
        if rclpy.ok():
            rclpy.shutdown()


if __name__ == '__main__':
    main()
